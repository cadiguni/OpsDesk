using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Import;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Conciliação dos chamados abertos pela caixa de e-mail com a exportação do Spiceworks
/// (<c>POST /api/admin/import/spiceworks</c>): data real, fechado lá fica fechado aqui, e
/// nada disso vira aviso ao solicitante nem ação no histórico.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SpiceworksReconciliationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private static readonly DateTimeOffset January = new(2025, 1, 14, 13, 10, 39, TimeSpan.Zero);

    [Fact]
    public async Task Corrige_a_data_fecha_o_que_foi_fechado_la_e_nao_avisa_ninguem()
    {
        var world = await SetUpAsync();
        var closedThere = await EmailTicketAsync(world, "<fechado@outlook.com>");
        var openThere = await EmailTicketAsync(world, "<aberto@outlook.com>");
        await ChangeStatusAsync(world.Technician, openThere, TicketStatus.InProgress);

        var export = Export(
            Item(1, "fechado@outlook.com", "Closed", January, closedAt: January.AddHours(5), firstResponse: 3600),
            Item(2, "aberto@outlook.com", "Open", January.AddDays(1)),
            Item(3, "sem-par@outlook.com", "Open", January.AddDays(2)),
            Item(4, null, "Closed", January.AddDays(3)));

        var preview = await ReconcileAsync(world.Manager, export, dryRun: true);

        Assert.Equal(4, preview.Tickets);
        Assert.Equal(1, preview.WithoutMessageId);
        Assert.Equal(2, preview.Matched);
        Assert.Equal(1, preview.NotFound);
        Assert.Equal(2, preview.DatesFixed);
        Assert.Equal(1, preview.Closed);
        Assert.Equal(1, preview.StillOpen);
        Assert.Equal(3, Assert.Single(preview.Notes).SpiceworksNumber);
        Assert.Equal(TicketStatus.Open, (await GetAsync(world.Technician, closedThere)).Status);

        var (notifications, emails, history) = await CountsAsync();

        var applied = await ReconcileAsync(world.Manager, export, dryRun: false);
        Assert.Equal(1, applied.Closed);

        await using (var db = Fixture.CreateContext())
        {
            var closed = await db.Tickets.SingleAsync(t => t.Id == closedThere);
            Assert.Equal(TicketStatus.Closed, closed.Status);
            Assert.Equal(January, closed.CreatedAt);
            Assert.Equal(January.AddHours(5), closed.ResolvedAt);
            Assert.Equal(January.AddHours(5), closed.ClosedAt);
            Assert.Equal(January.AddHours(1), closed.FirstRespondedAt);
            // O prazo volta a contar da abertura real: está em janeiro de 2025, não hoje.
            Assert.True(closed.SlaResolutionDueAt < January.AddDays(30));

            var open = await db.Tickets.SingleAsync(t => t.Id == openThere);
            Assert.Equal(TicketStatus.InProgress, open.Status);
            Assert.Equal(January.AddDays(1), open.CreatedAt);
        }

        // Correção de origem, não atendimento: sem aviso, sem e-mail e sem linha no histórico.
        Assert.Equal((notifications, emails, history), await CountsAsync());

        var again = await ReconcileAsync(world.Manager, export, dryRun: false);
        Assert.Equal(0, again.DatesFixed);
        Assert.Equal(0, again.Closed);
    }

    [Fact]
    public async Task Fechado_aqui_antes_da_conciliacao_recebe_a_data_de_la()
    {
        var world = await SetUpAsync();
        var ticket = await EmailTicketAsync(world, "<lote@outlook.com>");
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Closed);

        await ReconcileAsync(world.Manager, Export(Item(1, "lote@outlook.com", "Closed", January, closedAt: January.AddDays(2))), dryRun: false);

        await using var db = Fixture.CreateContext();
        var closed = await db.Tickets.SingleAsync(t => t.Id == ticket);
        Assert.Equal(TicketStatus.Closed, closed.Status);
        Assert.Equal(January.AddDays(2), closed.ClosedAt);
        Assert.Equal(January.AddDays(2), closed.ResolvedAt);
    }

    [Fact]
    public async Task So_administrador_concilia_e_arquivo_que_nao_e_do_spiceworks_e_recusado()
    {
        var world = await SetUpAsync();

        var technician = await PostAsync(world.Technician, Export(), dryRun: true);
        var wrongFile = await PostAsync(world.Manager, "[{\"titulo\":\"x\"}]", dryRun: true);

        Assert.Equal(HttpStatusCode.Forbidden, technician.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongFile.StatusCode);
    }

    // ----- Atalhos -----

    /// <summary>Chamado como a caixa de e-mail abre: origem E-mail, Message-ID como identificador.</summary>
    private async Task<Guid> EmailTicketAsync(World world, string messageId)
    {
        var id = await OpenTicketAsync(world);

        await using var db = Fixture.CreateContext();
        await db.Tickets.Where(t => t.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.Source, TicketSource.Email)
            .SetProperty(t => t.ExternalId, messageId));

        return id;
    }

    private async Task<(int, int, int)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();

        return (await db.Notifications.CountAsync(), await db.OutboundEmails.CountAsync(), await db.TicketHistory.CountAsync());
    }

    private static string Item(
        int number, string? messageId, string status, DateTimeOffset created, DateTimeOffset? closedAt = null, int? firstResponse = null)
    {
        static string Date(DateTimeOffset value) => $"\"{value.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC\"";

        return $$"""
            {
              "ticket_number": {{number}},
              "status": "{{status}}",
              "summary": "Chamado {{number}}",
              "email_message_id": {{(messageId is null ? "null" : $"\"{messageId}\"")}},
              "created_at": {{Date(created)}},
              "updated_at": {{Date(closedAt ?? created)}},
              "closed_at": {{(closedAt is { } c ? Date(c) : "null")}},
              "first_response_secs": {{(firstResponse?.ToString() ?? "null")}},
              "Comments": []
            }
            """;
    }

    private static string Export(params string[] items) =>
        $$"""{ "users": [], "tickets": [{{string.Join(",", items)}}] }""";

    private static async Task<SpiceworksReconciliationReport> ReconcileAsync(HttpClient client, string content, bool dryRun)
    {
        var response = await PostAsync(client, content, dryRun);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<SpiceworksReconciliationReport>())!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string content, bool dryRun)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", "ticket_export.json");

        return client.PostAsync($"/api/admin/import/spiceworks?dryRun={dryRun.ToString().ToLowerInvariant()}", form);
    }
}
