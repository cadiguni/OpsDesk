using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Dashboard;

/// <summary>Contagem de chamados por um rótulo qualquer.</summary>
public record CountByLabel(string Label, int Count);

/// <summary>Indicadores da seção 13.7 do README.</summary>
public record DashboardSummary(
    int Total,
    int Open,
    int InProgress,
    int WaitingOnRequester,
    int Resolved,
    int OverdueResponse,
    int OverdueResolution,
    IReadOnlyList<CountByLabel> ByStatus,
    IReadOnlyList<CountByLabel> ByPriority,
    IReadOnlyList<CountByLabel> ByCategory,
    IReadOnlyList<CountByLabel> ByTechnician);

/// <summary>
/// Indicadores do dashboard, cada um por consulta agregada dedicada.
///
/// Nenhum chamado é carregado para ser contado em memória — é convenção explícita do
/// CLAUDE.md, e não é preciosismo: contar no cliente significa transferir a tabela de
/// chamados a cada abertura da tela, e o custo cresce com a operação enquanto a resposta
/// do banco a um <c>COUNT</c> agrupado praticamente não cresce.
///
/// As consultas passam por <c>VisibleTo</c> mesmo o dashboard sendo de gestor, que vê
/// tudo. É redundante hoje e barato: se algum dia a tela for liberada para técnico, os
/// números já saem corretos para o que aquele perfil pode ver, em vez de vazarem o total
/// da operação.
/// </summary>
public class DashboardService(IOpsDeskDbContext db, IClock clock)
{
    public async Task<DashboardSummary> GetSummaryAsync(
        TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var visible = db.Tickets.AsNoTracking().VisibleTo(viewer);

        // Uma varredura agrupada por status resolve o total e os contadores por status.
        var byStatus = await visible
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countOf = byStatus.ToDictionary(x => x.Status, x => x.Count);

        // Cancelado fica fora dos indicadores de SLA (README, 8.3), e por isso também sai
        // das duas contagens de vencidos abaixo.
        var overdueResponse = await visible.CountAsync(
            t => t.Status != TicketStatus.Cancelled
                 && t.FirstRespondedAt == null
                 && t.SlaResponseDueAt < now,
            cancellationToken);

        var overdueResolution = await visible.CountAsync(
            t => t.Status != TicketStatus.Cancelled
                 && t.ResolvedAt == null
                 && t.SlaResolutionDueAt < now,
            cancellationToken);

        var byPriority = await visible
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // A ordenação é pelo agregado, antes da projeção.
        //
        // Ordenar por `x.Count` depois de projetar em CountByLabel não traduz: o EF Core
        // recusa a query inteira, com a mensagem apontando para o Join gerado pela
        // navegação, e não para a ordenação — o que torna o erro difícil de ler.
        var byCategory = await visible
            .GroupBy(t => t.Category.Name)
            .OrderByDescending(g => g.Count())
            .Select(g => new CountByLabel(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var byTechnician = await visible
            .Where(t => t.AssignedTechnicianId != null)
            .GroupBy(t => t.AssignedTechnician!.Name)
            .OrderByDescending(g => g.Count())
            .Select(g => new CountByLabel(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var unassigned = await visible.CountAsync(
            t => t.AssignedTechnicianId == null && !ClosedOut.Contains(t.Status), cancellationToken);

        return new DashboardSummary(
            Total: byStatus.Sum(x => x.Count),
            Open: countOf.GetValueOrDefault(TicketStatus.Open),
            InProgress: countOf.GetValueOrDefault(TicketStatus.InProgress),
            WaitingOnRequester: countOf.GetValueOrDefault(TicketStatus.WaitingOnRequester),
            Resolved: countOf.GetValueOrDefault(TicketStatus.Resolved),
            OverdueResponse: overdueResponse,
            OverdueResolution: overdueResolution,

            // Os rótulos saem em ordem de ciclo de vida, e não na ordem que o banco
            // devolveu, para o gráfico não trocar as colunas de lugar entre duas cargas.
            ByStatus:
            [
                .. Enum.GetValues<TicketStatus>()
                    .Select(status => new CountByLabel(
                        status.ToString(), countOf.GetValueOrDefault(status)))
            ],
            ByPriority:
            [
                .. Enum.GetValues<TicketPriority>()
                    .Select(priority => new CountByLabel(
                        priority.ToString(),
                        byPriority.FirstOrDefault(x => x.Priority == priority)?.Count ?? 0))
            ],
            ByCategory: byCategory,
            ByTechnician:
            [
                .. byTechnician,

                // Sem responsável entra como uma linha própria: é a fila que o painel do
                // técnico usa, e omiti-la faria o gráfico por técnico parecer cobrir toda
                // a operação quando não cobre.
                new CountByLabel("Sem responsável", unassigned)
            ]);
    }

    private static readonly TicketStatus[] ClosedOut =
        [TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled];
}
