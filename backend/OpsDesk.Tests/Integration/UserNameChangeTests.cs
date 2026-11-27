using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Troca do nome exibido pelo administrador. Vale para a própria conta — é assim que o
/// administrador do bootstrap deixa de se chamar pelo nome genérico — e só para ele.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserNameChangeTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Administrador_troca_o_proprio_nome_e_o_de_outra_pessoa()
    {
        var world = await SetUpAsync();

        var mine = await ChangeNameAsync(world.Manager, world.ManagerId, "  Maria Souza  ");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("Maria Souza", (await mine.Content.ReadJsonAsync<ManagedUser>())!.Name);

        Assert.Equal(HttpStatusCode.OK, (await ChangeNameAsync(world.Manager, world.TechnicianId, "João Lima")).StatusCode);

        await using var db = Fixture.CreateContext();
        var me = await db.Users.SingleAsync(u => u.Id == world.ManagerId);
        Assert.Equal("Maria Souza", me.Name);
        Assert.Equal(UserRole.Manager, me.Role);
        Assert.Equal("João Lima", (await db.Users.SingleAsync(u => u.Id == world.TechnicianId)).Name);
    }

    [Fact]
    public async Task Tecnico_e_solicitante_nao_trocam_nome()
    {
        var world = await SetUpAsync();

        await using var before = Fixture.CreateContext();
        var original = (await before.Users.SingleAsync(u => u.Id == world.RequesterId)).Name;

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ChangeNameAsync(client, world.RequesterId, "Outro nome")).StatusCode);
        }

        await using var db = Fixture.CreateContext();
        Assert.Equal(original, (await db.Users.SingleAsync(u => u.Id == world.RequesterId)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nome_vazio_e_recusado(string name)
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeNameAsync(world.Manager, world.TechnicianId, name)).StatusCode);
    }

    [Fact]
    public async Task Pessoa_inexistente_responde_404()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await ChangeNameAsync(world.Manager, Guid.NewGuid(), "Alguém")).StatusCode);
    }

    [Fact]
    public async Task Trilha_guarda_o_nome_antigo_e_o_novo_inteiros()
    {
        var world = await SetUpAsync();

        await using var before = Fixture.CreateContext();
        var original = (await before.Users.SingleAsync(u => u.Id == world.TechnicianId)).Name;

        // Mais de 100 caracteres: a coluna da trilha era menor que a do nome.
        var longName = new string('a', 150);
        await ChangeNameAsync(world.Manager, world.TechnicianId, longName);

        // Repetir o mesmo nome não gera evento.
        await ChangeNameAsync(world.Manager, world.TechnicianId, longName);

        var response = await world.Manager.GetAsync($"/api/admin/users/{world.TechnicianId}/history");
        var changes = (await response.Content.ReadJsonAsync<List<UserAuditItem>>())!
            .Where(h => h.Action == UserAuditAction.NameChanged)
            .ToList();

        var change = Assert.Single(changes);
        Assert.Equal(original, change.PreviousValue);
        Assert.Equal(longName, change.NewValue);
        Assert.NotNull(change.ChangedByName);
    }

    private static Task<HttpResponseMessage> ChangeNameAsync(HttpClient client, Guid userId, string name) =>
        client.PostAsJsonAsync($"/api/admin/users/{userId}/name", new ChangeNameRequest(name));
}
