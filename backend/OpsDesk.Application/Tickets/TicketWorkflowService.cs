using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Mudança de status e atribuição de responsável.
///
/// A validade da transição é decidida pelo <see cref="TicketStatusMachine"/>, não aqui:
/// este serviço aplica os efeitos colaterais da mudança — pausa e retomada do SLA, e as
/// datas de resolução e fechamento.
/// </summary>
public class TicketWorkflowService(
    IOpsDeskDbContext db, SlaClock sla, TicketService tickets, IClock clock)
{
    public async Task<ChangeStatusResult> ChangeStatusAsync(
        Guid ticketId,
        ChangeStatusRequest request,
        TicketViewer viewer,
        CancellationToken cancellationToken = default)
    {
        var ticket = await db.Tickets
            .VisibleTo(viewer)
            .SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

        if (ticket is null)
        {
            return new ChangeStatusResult.TicketNotFound();
        }

        var from = ticket.Status;
        var to = request.Status;

        // Uma verificação só, cobrindo grafo e perfil. Duplicar a regra aqui seria criar
        // uma segunda fonte de verdade que envelheceria em silêncio.
        if (!TicketStatusMachine.IsAllowedForRole(from, to, viewer.Role))
        {
            return new ChangeStatusResult.TransitionRejected(from, to);
        }

        // O solicitante só mexe no próprio chamado. O filtro de visibilidade já garante
        // isso para ele, mas a verificação fica explícita: se algum dia o filtro do
        // solicitante mudar, é aqui que a intenção está escrita.
        if (viewer.Role == UserRole.Requester && ticket.RequesterId != viewer.UserId)
        {
            return new ChangeStatusResult.TicketNotFound();
        }

        var now = clock.UtcNow;

        ApplySlaEffects(ticket, from, to, now);

        ticket.Status = to;

        // O histórico da mudança é gravado pelo interceptor, comparando o valor anterior
        // com o novo. Nada é adicionado à mão.
        await db.SaveChangesAsync(cancellationToken);

        var detail = await tickets.GetAsync(ticket.Id, viewer, cancellationToken);

        return new ChangeStatusResult.Changed(detail!);
    }

    /// <summary>
    /// Efeitos da transição sobre o relógio de SLA e sobre as datas do chamado.
    /// Regras da seção 8.2 e 8.3 do README.
    /// </summary>
    internal void ApplySlaEffects(Ticket ticket, TicketStatus from, TicketStatus to, DateTimeOffset now)
    {
        // Sair de "Aguardando usuário" empurra o prazo de resolução pelo tempo útil que o
        // chamado passou esperando. Vem primeiro: resolver um chamado pausado precisa
        // encerrar a pausa antes de datar a resolução.
        if (TicketStatusMachine.PausesResolutionSla(from))
        {
            sla.Resume(ticket, now);
        }

        if (TicketStatusMachine.PausesResolutionSla(to))
        {
            sla.Pause(ticket, now);
        }

        switch (to)
        {
            case TicketStatus.Resolved:
                ticket.ResolvedAt = now;
                break;

            case TicketStatus.Closed:
                ticket.ResolvedAt ??= now;
                ticket.ClosedAt = now;
                break;

            case TicketStatus.InProgress when from == TicketStatus.Resolved:
                // Reabertura por retrabalho: a solução não resolveu. Limpamos a data de
                // resolução porque o chamado voltou a estar em aberto — deixá-la
                // preenchida faria o chamado nunca mais aparecer como vencido, por mais
                // que se arrastasse.
                //
                // Não se perde informação: a transição para "Resolvido" está no
                // TicketHistory com data e autor, que é a trilha de auditoria de verdade.
                ticket.ResolvedAt = null;
                break;
        }

        // Cancelar encerra a contagem e não faz sentido manter pausa pendente.
        if (to == TicketStatus.Cancelled)
        {
            ticket.SlaPausedAt = null;
        }
    }

    /// <summary>
    /// Troca prioridade e categoria — a reclassificação da triagem.
    ///
    /// Prioridade não é só rótulo: ela define o prazo, então trocá-la recalcula o SLA pelo
    /// <see cref="SlaClock.Recalculate"/>. Categoria não mexe em prazo nenhum, só em
    /// roteamento e relatório.
    ///
    /// O histórico das duas mudanças é gravado pelo interceptor, que compara valor
    /// anterior com novo. Nada é adicionado à mão aqui.
    /// </summary>
    public async Task<ChangeClassificationResult> ChangeClassificationAsync(
        Guid ticketId,
        ChangeClassificationRequest request,
        TicketViewer viewer,
        CancellationToken cancellationToken = default)
    {
        if (!viewer.IsStaff)
        {
            // Prioridade definida pelo solicitante depois da abertura faria da fila uma
            // negociação com quem abre o chamado. A triagem é da equipe (README, 4.1).
            return new ChangeClassificationResult.NotAllowed();
        }

        var ticket = await db.Tickets
            .VisibleTo(viewer)
            .SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

        if (ticket is null)
        {
            return new ChangeClassificationResult.TicketNotFound();
        }

        if (ticket.Status is TicketStatus.Closed or TicketStatus.Cancelled)
        {
            return new ChangeClassificationResult.TicketClosed();
        }

        if (request.CategoryId is { } categoryId && categoryId != ticket.CategoryId)
        {
            var categoryExists = await db.Categories
                .AnyAsync(c => c.Id == categoryId && c.IsActive, cancellationToken);

            if (!categoryExists)
            {
                return new ChangeClassificationResult.CategoryNotFound();
            }

            ticket.CategoryId = categoryId;
        }

        if (request.Priority is { } priority && priority != ticket.Priority)
        {
            var policy = await db.SlaPolicies
                .SingleOrDefaultAsync(p => p.Priority == priority && p.IsActive, cancellationToken);

            if (policy is null)
            {
                // Mesmo tratamento da abertura: sem política ativa não há prazo, e é erro
                // de configuração do sistema, não do pedido.
                return new ChangeClassificationResult.SlaPolicyMissing(priority);
            }

            ticket.Priority = priority;
            sla.Recalculate(ticket, policy);
        }

        await db.SaveChangesAsync(cancellationToken);

        var detail = await tickets.GetAsync(ticket.Id, viewer, cancellationToken);

        return new ChangeClassificationResult.Changed(detail!);
    }

    public async Task<AssignResult> AssignAsync(
        Guid ticketId,
        AssignRequest request,
        TicketViewer viewer,
        CancellationToken cancellationToken = default)
    {
        if (!viewer.IsStaff)
        {
            return new AssignResult.NotAllowed("Apenas técnicos e gestores atribuem responsável.");
        }

        var ticket = await db.Tickets
            .VisibleTo(viewer)
            .SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

        if (ticket is null)
        {
            return new AssignResult.TicketNotFound();
        }

        // Técnico e gestor repassam chamado dentro da equipe. O filtro de visibilidade já
        // limita *quais* chamados um técnico alcança — os sem responsável e os seus —, e
        // dentro desse conjunto encaminhar para o colega certo é trabalho normal de
        // atendimento, não privilégio de gestão. Quem não é da equipe não chega aqui.

        if (request.TechnicianId is { } technicianId)
        {
            var isStaff = await db.Users.AnyAsync(
                u => u.Id == technicianId
                     && u.IsActive
                     && (u.Role == UserRole.Technician || u.Role == UserRole.Manager),
                cancellationToken);

            if (!isStaff)
            {
                return new AssignResult.TechnicianInvalid();
            }
        }

        // Atribuição mexe só no responsável, nunca no status.
        //
        // A seção 10 do README diz que o status "poderá" mudar para "Em atendimento" ao
        // assumir. Optamos por não fazer isso implicitamente: a máquina de estados é a
        // única coisa que muda status no sistema, e uma ação que muda dois campos por
        // conta própria torna o histórico mais difícil de ler e a operação mais difícil de
        // prever. Assumir e iniciar atendimento são dois cliques, e cada um aparece
        // separado na trilha de auditoria.
        ticket.AssignedTechnicianId = request.TechnicianId;

        await db.SaveChangesAsync(cancellationToken);

        var detail = await tickets.GetAsync(ticket.Id, viewer, cancellationToken);

        // Encaminhar para outro técnico tira o chamado da própria visibilidade: técnico vê
        // o que está sem responsável e o que é dele, e o chamado deixou de ser os dois.
        // A atribuição valeu; só não há mais o que devolver. Reler o chamado ignorando o
        // filtro para poder responder alguma coisa furaria a invariante 1 por conveniência
        // de interface.
        return detail is null
            ? new AssignResult.AssignedAndHidden()
            : new AssignResult.Assigned(detail);
    }

    /// <summary>Técnicos e gestores ativos, para o seletor de responsável.</summary>
    public Task<List<StaffOption>> ListStaffAsync(CancellationToken cancellationToken = default) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Technician || u.Role == UserRole.Manager))
            .OrderBy(u => u.Name)
            .Select(u => new StaffOption(u.Id, u.Name, u.Role))
            .ToListAsync(cancellationToken);
}
