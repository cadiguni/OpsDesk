using System.Net;
using System.Net.Http.Json;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Readiness separado de liveness.
///
/// O caso que motiva isto: instalação em que ninguém rodou <c>--setup</c>. O banco
/// responde, o <c>/health</c> devolve 200, o orquestrador manda tráfego — e todo chamado
/// novo falha com 500, porque não existe política de SLA para calcular prazo.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ReadinessTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Banco_vazio_reprova_no_ready_e_passa_no_health()
    {
        // Schema aplicado pela fixture, dados de referência ausentes: exatamente o estado
        // de quem subiu a aplicação e esqueceu o seed.
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        // O liveness continua verde de propósito. Reiniciar o contêiner não cria
        // categoria nem política de SLA — reprovar aqui daria laço de reinício sem
        // diagnóstico nenhum.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var ready = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task O_ready_diz_o_que_falta()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var report = await (await client.GetAsync("/ready")).Content.ReadFromJsonAsync<Report>();

        // O corpo existe para ser lido por quem está no deploy. "Unhealthy" sozinho, que é
        // o que o escritor padrão devolve, não diz o que fazer.
        Assert.Equal("Unhealthy", report?.Status);
        Assert.Contains("--seed", report!.Checks["reference-data"].Description);
        Assert.Equal("Healthy", report.Checks["schema"].Status);
    }

    [Fact]
    public async Task Instalacao_completa_fica_pronta()
    {
        await fixture.ResetAsync();

        await using (var db = fixture.CreateContext())
        {
            await TestData.NewSeeder(db).SeedAsync(includeDevelopmentUsers: false);
        }

        using var client = fixture.Api.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
    }

    private record Report(string Status, Dictionary<string, Entry> Checks);

    private record Entry(string Status, string Description);
}
