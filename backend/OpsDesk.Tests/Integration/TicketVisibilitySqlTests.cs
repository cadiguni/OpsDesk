using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Invariantes 1 e 2 do CLAUDE.md contra PostgreSQL de verdade.
///
/// Os testes de unidade provam a regra sobre objetos em memória; estes provam que ela
/// sobrevive à tradução para SQL. É a diferença que importa: um predicado que o EF não
/// consegue traduzir e avalia no cliente continuaria "correto" no teste de unidade e
/// traria as linhas de todo mundo do banco.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketVisibilitySqlTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Solicitante_so_enxerga_os_proprios_chamados()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var visible = await db.Tickets
            .VisibleTo(new TicketViewer(s.Requester.Id, UserRole.Requester))
            .Select(t => t.Title)
            .ToListAsync();

        Assert.Equal(["Chamado do solicitante"], visible);
    }

    [Fact]
    public async Task Buscar_chamado_de_terceiro_por_id_nao_devolve_nada()
    {
        // O caso que mais interessa: adivinhar o id de outro chamado não dá acesso,
        // porque o filtro entra antes do Where do id.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var ticket = await db.Tickets
            .VisibleTo(new TicketViewer(s.Requester.Id, UserRole.Requester))
            .SingleOrDefaultAsync(t => t.Id == s.OfSomeoneElse.Id);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task Tecnico_enxerga_a_fila_inteira()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var visible = await db.Tickets
            .VisibleTo(new TicketViewer(s.Technician.Id, UserRole.Technician))
            .Select(t => t.Title)
            .ToListAsync();

        Assert.Contains("Chamado de terceiro", visible);
        Assert.Contains("Sem responsável", visible);
        Assert.Contains("Chamado do solicitante", visible);

        // O que era proibido e passou a ser permitido: chamado que outro técnico assumiu.
        Assert.Contains("De outro técnico", visible);
    }

    [Fact]
    public async Task Gestor_enxerga_todos_os_chamados()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var count = await db.Tickets
            .VisibleTo(new TicketViewer(s.Manager.Id, UserRole.Manager))
            .CountAsync();

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Comentario_interno_nao_sai_do_banco_para_o_solicitante()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        db.AddRange(
            new TicketComment
            {
                TicketId = s.Own.Id,
                AuthorId = s.Technician.Id,
                Content = "Poderia enviar um print do erro?",
                IsInternal = false
            },
            new TicketComment
            {
                TicketId = s.Own.Id,
                AuthorId = s.Technician.Id,
                Content = "Verificar se esse usuário está no grupo correto do AD.",
                IsInternal = true
            });
        await db.SaveChangesAsync();

        var visible = await db.TicketComments
            .OfTicketVisibleTo(s.Own.Id, new TicketViewer(s.Requester.Id, UserRole.Requester))
            .Select(c => new { c.Content, c.IsInternal })
            .ToListAsync();

        Assert.Single(visible);
        Assert.False(visible[0].IsInternal);
        Assert.DoesNotContain("AD", visible[0].Content);
    }

    [Fact]
    public async Task Equipe_enxerga_o_comentario_interno()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        db.Add(new TicketComment
        {
            TicketId = s.OfSomeoneElse.Id,
            AuthorId = s.Technician.Id,
            Content = "Observação interna.",
            IsInternal = true
        });
        await db.SaveChangesAsync();

        var asTechnician = await db.TicketComments
            .OfTicketVisibleTo(s.OfSomeoneElse.Id, new TicketViewer(s.Technician.Id, UserRole.Technician))
            .CountAsync();

        var asManager = await db.TicketComments
            .OfTicketVisibleTo(s.OfSomeoneElse.Id, new TicketViewer(s.Manager.Id, UserRole.Manager))
            .CountAsync();

        Assert.Equal(1, asTechnician);
        Assert.Equal(1, asManager);
    }

    [Fact]
    public async Task Solicitante_nao_le_historico_de_chamado_de_terceiro()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var visible = await db.TicketHistory
            .VisibleTo(new TicketViewer(s.Requester.Id, UserRole.Requester))
            .Select(h => h.TicketId)
            .Distinct()
            .ToListAsync();

        Assert.Equal([s.Own.Id], visible);
    }
}
