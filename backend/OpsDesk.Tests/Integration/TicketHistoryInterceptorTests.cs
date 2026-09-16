using Microsoft.EntityFrameworkCore;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Invariante 3 do CLAUDE.md e decisão 4.2 de docs/arquitetura.md: o histórico é gravado
/// pelo interceptor e é append-only.
///
/// O ponto destes testes não é que o histórico funciona quando alguém se lembra dele — é
/// que ele acontece sem ninguém pedir, a partir de um <c>SaveChanges</c> comum.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketHistoryInterceptorTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Criacao_do_chamado_gera_historico_sem_o_servico_pedir()
    {
        await fixture.ResetAsync();

        await using var setup = fixture.CreateContext();
        var requester = TestData.NewUser(UserRole.Requester);
        var category = TestData.NewCategory();
        setup.AddRange(requester, category);
        await setup.SaveChangesAsync();

        await using var db = fixture.CreateContext(currentUserId: requester.Id);
        var ticket = TestData.NewTicket(requester, category);
        db.Add(ticket);
        await db.SaveChangesAsync();

        var history = await db.TicketHistory
            .Where(h => h.TicketId == ticket.Id)
            .ToListAsync();

        var entry = Assert.Single(history);
        Assert.Equal(TicketHistoryAction.Created, entry.Action);
        Assert.Equal(nameof(TicketStatus.Open), entry.NewValue);
        Assert.Equal(requester.Id, entry.ChangedById);
    }

    [Fact]
    public async Task Mudanca_de_status_gera_um_registro_com_valor_anterior_e_novo()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Technician.Id);
        var ticket = await change.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.Status = TicketStatus.InProgress;
        await change.SaveChangesAsync();

        var entry = await change.TicketHistory
            .Where(h => h.TicketId == ticket.Id && h.Action == TicketHistoryAction.StatusChanged)
            .SingleAsync();

        Assert.Equal(nameof(TicketStatus.Open), entry.PreviousValue);
        Assert.Equal(nameof(TicketStatus.InProgress), entry.NewValue);
        Assert.Equal(s.Technician.Id, entry.ChangedById);
    }

    [Fact]
    public async Task Resolver_fechar_e_cancelar_tem_acao_propria()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Technician.Id);
        var ticket = await change.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.Status = TicketStatus.Resolved;
        await change.SaveChangesAsync();

        var actions = await change.TicketHistory
            .Where(h => h.TicketId == ticket.Id)
            .Select(h => h.Action)
            .ToListAsync();

        // Um registro por mudança: a ação específica, nunca ela mais um StatusChanged.
        Assert.Contains(TicketHistoryAction.Resolved, actions);
        Assert.DoesNotContain(TicketHistoryAction.StatusChanged, actions);
    }

    [Fact]
    public async Task Varias_mudancas_no_mesmo_save_geram_um_registro_por_campo()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var newCategory = TestData.NewCategory();
        db.Add(newCategory);
        await db.SaveChangesAsync();

        await using var change = fixture.CreateContext(currentUserId: s.Manager.Id);
        var ticket = await change.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.Status = TicketStatus.InProgress;
        ticket.Priority = TicketPriority.Critical;
        ticket.CategoryId = newCategory.Id;
        ticket.AssignedTechnicianId = s.Technician.Id;
        await change.SaveChangesAsync();

        var actions = await change.TicketHistory
            .Where(h => h.TicketId == ticket.Id && h.Action != TicketHistoryAction.Created)
            .Select(h => h.Action)
            .ToListAsync();

        Assert.Equal(4, actions.Count);
        Assert.Contains(TicketHistoryAction.StatusChanged, actions);
        Assert.Contains(TicketHistoryAction.PriorityChanged, actions);
        Assert.Contains(TicketHistoryAction.CategoryChanged, actions);
        Assert.Contains(TicketHistoryAction.TechnicianAssigned, actions);
    }

    [Fact]
    public async Task Remover_o_responsavel_e_registrado_como_desatribuicao()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Manager.Id);
        var ticket = await change.Tickets.SingleAsync(t => t.Id == s.OfSomeoneElse.Id);
        ticket.AssignedTechnicianId = null;
        await change.SaveChangesAsync();

        var entry = await change.TicketHistory
            .Where(h => h.TicketId == ticket.Id
                        && h.Action == TicketHistoryAction.TechnicianUnassigned)
            .SingleAsync();

        Assert.Equal(s.Technician.Id.ToString(), entry.PreviousValue);
        Assert.Null(entry.NewValue);
    }

    [Fact]
    public async Task Salvar_sem_mudar_nada_relevante_nao_polui_o_historico()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Manager.Id);
        var ticket = await change.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.Title = "Título corrigido";
        await change.SaveChangesAsync();

        var count = await change.TicketHistory
            .Where(h => h.TicketId == ticket.Id && h.Action != TicketHistoryAction.Created)
            .CountAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Comentario_gera_historico_distinguindo_publico_de_interno()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Technician.Id);
        change.AddRange(
            new TicketComment
            {
                TicketId = s.Own.Id,
                AuthorId = s.Technician.Id,
                Content = "Pode testar novamente?",
                IsInternal = false
            },
            new TicketComment
            {
                TicketId = s.Own.Id,
                AuthorId = s.Technician.Id,
                Content = "Checar grupo do AD.",
                IsInternal = true
            });
        await change.SaveChangesAsync();

        var actions = await change.TicketHistory
            .Where(h => h.TicketId == s.Own.Id && h.Action != TicketHistoryAction.Created)
            .Select(h => h.Action)
            .ToListAsync();

        Assert.Contains(TicketHistoryAction.CommentAdded, actions);
        Assert.Contains(TicketHistoryAction.InternalCommentAdded, actions);
    }

    [Fact]
    public async Task O_texto_do_comentario_nao_e_copiado_para_o_historico()
    {
        // Duplicar o texto espalharia comentário interno por uma tabela com outra regra
        // de visibilidade, e o histórico deixaria de ser só a trilha de quem mexeu em quê.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var change = fixture.CreateContext(currentUserId: s.Technician.Id);
        change.Add(new TicketComment
        {
            TicketId = s.Own.Id,
            AuthorId = s.Technician.Id,
            Content = "segredo-da-equipe",
            IsInternal = true
        });
        await change.SaveChangesAsync();

        var values = await change.TicketHistory
            .Where(h => h.TicketId == s.Own.Id)
            .Select(h => h.PreviousValue + h.NewValue)
            .ToListAsync();

        Assert.DoesNotContain(values, v => v is not null && v.Contains("segredo"));
    }

    [Fact]
    public async Task Alterar_historico_e_recusado()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var tamper = fixture.CreateContext(currentUserId: s.Manager.Id);
        var entry = await tamper.TicketHistory.FirstAsync(h => h.TicketId == s.Own.Id);
        entry.NewValue = "valor reescrito";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamper.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public async Task Excluir_historico_e_recusado()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var tamper = fixture.CreateContext(currentUserId: s.Manager.Id);
        var entry = await tamper.TicketHistory.FirstAsync(h => h.TicketId == s.Own.Id);
        tamper.Remove(entry);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamper.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message);
    }
}
