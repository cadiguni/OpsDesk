using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Categories;
using OpsDesk.Application.Common;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Administração de categorias pelo gestor.
///
/// O que importa é o que desativar e renomear *não* fazem: não invalidam chamado
/// existente, não deixam a instalação sem categoria, e não são desfeitos pelo seed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CategoryAdministrationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Acesso -----

    [Fact]
    public async Task Tecnico_e_solicitante_nao_administram_categorias()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/admin/categories")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await CreateAsync(client, "Telefonia")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await UpdateAsync(client, world.CategoryId, "Acesso remoto")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await ChangeActivationAsync(client, world.CategoryId, false)).StatusCode);
        }

        await using var db = Fixture.CreateContext();
        var vpn = await db.Categories.SingleAsync(c => c.Id == world.CategoryId);

        Assert.Equal("VPN", vpn.Name);
        Assert.True(vpn.IsActive);
        Assert.False(await db.Categories.AnyAsync(c => c.Name == "Telefonia"));
    }

    // ----- Criação e edição -----

    [Fact]
    public async Task Categoria_criada_aparece_nos_seletores()
    {
        var world = await SetUpAsync();

        var response = await CreateAsync(world.Manager, "  Telefonia  ", "Ramais e celulares");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await response.Content.ReadJsonAsync<ManagedCategory>())!;
        Assert.Equal("Telefonia", created.Name);
        Assert.True(created.IsActive);

        var options = await SelectableAsync(world.Requester);
        Assert.Contains(options, c => c.Id == created.Id);
    }

    [Theory]
    [InlineData("VPN")]
    [InlineData("vpn")]
    [InlineData(" Vpn ")]
    public async Task Nome_repetido_e_recusado_sem_diferenciar_maiusculas(string name)
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(world.Manager, name)).StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Categories.CountAsync(c => c.Name.ToLower() == "vpn"));
    }

    [Fact]
    public async Task Nome_de_categoria_desativada_tambem_esta_em_uso()
    {
        var world = await SetUpAsync();

        await ChangeActivationAsync(world.Manager, world.CategoryId, false);

        Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(world.Manager, "VPN")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nome_vazio_e_recusado(string name)
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(world.Manager, name)).StatusCode);
    }

    [Fact]
    public async Task Nome_acima_do_limite_da_coluna_e_recusado_com_400_e_nao_500()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CreateAsync(world.Manager, new string('x', 101))).StatusCode);
    }

    [Fact]
    public async Task Renomear_muda_o_nome_nos_chamados_existentes()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await UpdateAsync(world.Manager, world.CategoryId, "Acesso remoto", "VPN e afins");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Acesso remoto", (await GetAsync(world.Requester, ticket)).CategoryName);
    }

    [Fact]
    public async Task Renomear_para_nome_de_outra_categoria_e_recusado()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Conflict,
            (await UpdateAsync(world.Manager, world.CategoryId, "rede")).StatusCode);
    }

    [Fact]
    public async Task Trocar_so_a_caixa_do_proprio_nome_e_permitido()
    {
        var world = await SetUpAsync();

        var response = await UpdateAsync(world.Manager, world.CategoryId, "vpn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("vpn", (await response.Content.ReadJsonAsync<ManagedCategory>())!.Name);
    }

    [Fact]
    public async Task Categoria_inexistente_responde_404()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await UpdateAsync(world.Manager, Guid.NewGuid(), "Qualquer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ChangeActivationAsync(world.Manager, Guid.NewGuid(), false)).StatusCode);
    }

    // ----- Ativação -----

    [Fact]
    public async Task Desativada_sai_dos_seletores_e_os_chamados_continuam()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await ChangeActivationAsync(world.Manager, world.CategoryId, false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(await SelectableAsync(world.Requester), c => c.Id == world.CategoryId);

        // O chamado continua legível, com o nome da categoria, e continua sendo atendido.
        var detail = await GetAsync(world.Requester, ticket);
        Assert.Equal("VPN", detail.CategoryName);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);

        var admin = (await ListCategoriesAsync(world.Manager, "?isActive=false")).Items;
        var vpn = Assert.Single(admin);
        Assert.Equal(1, vpn.OpenTickets);
        Assert.Equal(1, vpn.TotalTickets);
    }

    [Fact]
    public async Task Categoria_desativada_nao_serve_para_abrir_nem_reclassificar()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await using (var db = Fixture.CreateContext())
        {
            // O chamado sai da VPN antes, para a reclassificação de volta ser uma troca real.
            var network = await db.Categories.SingleAsync(c => c.Name == "Rede");
            var moved = await world.Technician.PostAsJsonAsync(
                $"/api/tickets/{ticket}/classification", new ChangeClassificationRequest(null, network.Id));
            moved.EnsureSuccessStatusCode();
        }

        await ChangeActivationAsync(world.Manager, world.CategoryId, false);

        var open = await world.Requester.PostAsJsonAsync("/api/tickets", NewTicket(world.CategoryId));
        Assert.Equal(HttpStatusCode.BadRequest, open.StatusCode);

        var reclassify = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification", new ChangeClassificationRequest(null, world.CategoryId));
        Assert.Equal(HttpStatusCode.BadRequest, reclassify.StatusCode);
    }

    [Fact]
    public async Task Reativada_volta_aos_seletores()
    {
        var world = await SetUpAsync();

        await ChangeActivationAsync(world.Manager, world.CategoryId, false);
        var response = await ChangeActivationAsync(world.Manager, world.CategoryId, true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(await SelectableAsync(world.Requester), c => c.Id == world.CategoryId);
    }

    [Fact]
    public async Task Ultima_categoria_ativa_nao_pode_ser_desativada()
    {
        var world = await SetUpAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.Categories
                .Where(c => c.Id != world.CategoryId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.IsActive, false));
        }

        var response = await ChangeActivationAsync(world.Manager, world.CategoryId, false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Sem esta regra, o /ready reprovaria a instalação e tiraria a API do ar.
        using var anonymous = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/ready")).StatusCode);
    }

    // ----- Listagem -----

    [Fact]
    public async Task Listagem_filtra_por_situacao_e_busca_e_limita_a_pagina()
    {
        var world = await SetUpAsync();

        await ChangeActivationAsync(world.Manager, world.CategoryId, false);

        Assert.Equal(12, (await ListCategoriesAsync(world.Manager)).TotalCount);
        Assert.Equal(11, (await ListCategoriesAsync(world.Manager, "?isActive=true")).TotalCount);
        Assert.Equal("VPN", Assert.Single((await ListCategoriesAsync(world.Manager, "?search=vp")).Items).Name);
        Assert.Equal(PageRequest.MaxPageSize,
            (await ListCategoriesAsync(world.Manager, "?pageSize=5000")).PageSize);
    }

    // ----- Seed -----

    [Fact]
    public async Task Seed_nao_ressuscita_categoria_renomeada_nem_reativa_desativada()
    {
        var world = await SetUpAsync();

        await UpdateAsync(world.Manager, world.CategoryId, "Acesso remoto");

        await using (var db = Fixture.CreateContext())
        {
            var printer = await db.Categories.SingleAsync(c => c.Name == "Impressora");
            await ChangeActivationAsync(world.Manager, printer.Id, false);
        }

        await using (var db = Fixture.CreateContext())
        {
            await TestData.NewSeeder(db).SeedAsync(includeDevelopmentUsers: false);
        }

        await using (var db = Fixture.CreateContext())
        {
            Assert.False(await db.Categories.AnyAsync(c => c.Name == "VPN"));
            Assert.False((await db.Categories.SingleAsync(c => c.Name == "Impressora")).IsActive);
            Assert.Equal(12, await db.Categories.CountAsync());
        }
    }

    // ----- Atalhos -----

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, string? description = null) =>
        client.PostAsJsonAsync("/api/admin/categories", new SaveCategoryRequest(name, description));

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client, Guid id, string name, string? description = null) =>
        client.PutAsJsonAsync($"/api/admin/categories/{id}", new SaveCategoryRequest(name, description));

    private static Task<HttpResponseMessage> ChangeActivationAsync(HttpClient client, Guid id, bool isActive) =>
        client.PostAsJsonAsync($"/api/admin/categories/{id}/activation", new ChangeCategoryActivationRequest(isActive));

    private static async Task<PagedResult<ManagedCategory>> ListCategoriesAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/admin/categories{query}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<PagedResult<ManagedCategory>>())!;
    }

    private static async Task<List<CategoryOption>> SelectableAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<CategoryOption>>("/api/categories"))!;
}
