using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Holidays;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Feriados pelo gestor, e a renovação dos nacionais. O que importa é o efeito no
/// calendário: feriado cadastrado vale na hora, desligado deixa de valer, e a renovação
/// anual não desfaz a decisão de quem desligou.
/// </summary>
[Collection(PostgresCollection.Name)]
public class HolidayAdministrationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private static readonly int Year = DateTime.UtcNow.Year;

    [Fact]
    public async Task Lista_os_nacionais_do_ano()
    {
        var world = await SetUpAsync();

        var holidays = await ListAsync(world.Manager);

        Assert.Contains(holidays, h => h.Date == new DateOnly(Year, 12, 25) && !h.IsCustom && h.IsActive);
    }

    [Fact]
    public async Task Feriado_cadastrado_vale_na_hora_para_o_calendario()
    {
        var world = await SetUpAsync();
        var date = FreeWeekday();

        // Aquece o cache antes, para provar que o cadastro o invalida.
        Assert.DoesNotContain(date, Holidays());

        var response = await world.Manager.PostAsJsonAsync("/api/admin/holidays", new AddHolidayRequest(date, "Aniversário da cidade"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await response.Content.ReadJsonAsync<HolidayItem>())!.IsCustom);
        Assert.Contains(date, Holidays());
    }

    [Fact]
    public async Task Desligar_nacional_devolve_o_dia_ao_expediente_e_a_renovacao_respeita()
    {
        var world = await SetUpAsync();
        var christmas = Assert.Single(await ListAsync(world.Manager), h => h.Date == new DateOnly(Year, 12, 25));

        var response = await world.Manager.PostAsJsonAsync(
            $"/api/admin/holidays/{christmas.Id}/activation", new ChangeHolidayActivationRequest(false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(christmas.Date, Holidays());

        await RenewAsync();

        await using var db = Fixture.CreateContext();
        var stored = await db.Holidays.SingleAsync(h => h.Date == christmas.Date);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Nacional_nao_se_apaga_e_personalizado_sim()
    {
        var world = await SetUpAsync();
        var christmas = Assert.Single(await ListAsync(world.Manager), h => h.Date == new DateOnly(Year, 12, 25));

        Assert.Equal(HttpStatusCode.Conflict, (await world.Manager.DeleteAsync($"/api/admin/holidays/{christmas.Id}")).StatusCode);

        var created = await (await world.Manager.PostAsJsonAsync(
            "/api/admin/holidays", new AddHolidayRequest(FreeWeekday(), "Recesso"))).Content.ReadJsonAsync<HolidayItem>();

        Assert.Equal(HttpStatusCode.NoContent, (await world.Manager.DeleteAsync($"/api/admin/holidays/{created!.Id}")).StatusCode);
        Assert.DoesNotContain(created.Date, Holidays());
    }

    [Fact]
    public async Task Data_que_ja_tem_feriado_e_recusada()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PostAsJsonAsync(
            "/api/admin/holidays", new AddHolidayRequest(new DateOnly(Year, 12, 25), "Outro nome"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Renovacao_preenche_os_anos_seguintes()
    {
        await SetUpAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.Holidays.Where(h => h.Date.Year > Year).ExecuteDeleteAsync();
        }

        Assert.True(await RenewAsync() > 0);

        await using var after = Fixture.CreateContext();
        Assert.True(await after.Holidays.AnyAsync(h => h.Date == new DateOnly(Year + 2, 1, 1)));
    }

    [Fact]
    public async Task So_gestor_administra_feriados()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/holidays")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/admin/holidays", new AddHolidayRequest(FreeWeekday(), "x"))).StatusCode);
        }
    }

    // ----- Atalhos -----

    /// <summary>Um dia útil do ano que vem que não é feriado nacional: 10 de março ou vizinho.</summary>
    private static DateOnly FreeWeekday()
    {
        var day = new DateOnly(Year + 1, 3, 10);

        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private IReadOnlySet<DateOnly> Holidays()
    {
        using var scope = Fixture.Api.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IHolidayProvider>().GetHolidays();
    }

    private async Task<int> RenewAsync()
    {
        using var scope = Fixture.Api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.Seed.DatabaseSeeder>().RenewHolidaysAsync();
    }

    private static async Task<List<HolidayItem>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/admin/holidays?year={Year}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<HolidayItem>>())!;
    }
}
