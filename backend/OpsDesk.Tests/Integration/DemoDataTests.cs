using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Dados de demonstração: cobrem os estados que valem a pena mostrar, e nunca se misturam
/// com uma instalação em uso.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DemoDataTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Cria_chamados_em_todos_os_estados_que_importam()
    {
        await fixture.ResetAsync();

        Assert.Equal(DemoSeedResult.Created, await SeedAsync());

        await using var db = fixture.CreateContext();
        var statuses = await db.Tickets.Select(t => t.Status).Distinct().ToListAsync();

        foreach (var status in new[]
                 {
                     TicketStatus.Open, TicketStatus.Triage, TicketStatus.InProgress, TicketStatus.WaitingOnRequester,
                     TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled
                 })
        {
            Assert.Contains(status, statuses);
        }

        Assert.True(await db.Tickets.AnyAsync(t => t.SlaPausedAt != null));
        Assert.True(await db.TicketComments.AnyAsync(c => c.IsInternal));
        Assert.True(await db.Tickets.AnyAsync(t => t.ResolvedAt == null && t.Status != TicketStatus.Cancelled
                                                    && t.SlaResolutionDueAt < DateTimeOffset.UtcNow));

        // O anexo existe no banco e no armazenamento.
        var attachment = await db.TicketAttachments.SingleAsync();
        using var scope = fixture.Api.Services.CreateScope();
        await using var content = await scope.ServiceProvider.GetRequiredService<IAttachmentStorage>().OpenReadAsync(attachment.StorageKey);
        Assert.NotNull(content);
    }

    [Fact]
    public async Task Recusa_banco_que_ja_tem_chamados()
    {
        await fixture.ResetAsync();
        await SeedAsync();

        Assert.Equal(DemoSeedResult.RefusedDatabaseInUse, await SeedAsync());

        await using var db = fixture.CreateContext();
        Assert.Equal(8, await db.Tickets.CountAsync());
    }

    private async Task<DemoSeedResult> SeedAsync()
    {
        using var scope = fixture.Api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }
}
