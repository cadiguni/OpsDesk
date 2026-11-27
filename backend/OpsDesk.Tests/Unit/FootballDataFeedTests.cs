using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Fun;
using OpsDesk.Infrastructure.Fun;
using OpsDesk.Tests.Integration;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// A leitura da football-data.org: formato, cache e o que acontece quando ela cai. A cota
/// gratuita é pequena, então o que mais importa aqui é não perguntar duas vezes à toa.
/// </summary>
public class FootballDataFeedTests
{
    private const int Gremio = 1767;

    private const string StandingsJson = """
        {
          "competition": { "name": "Campeonato Brasileiro Série A", "code": "BSA" },
          "season": { "startDate": "2026-03-28", "currentMatchday": 36 },
          "standings": [
            { "type": "HOME", "table": [] },
            { "type": "TOTAL", "table": [
              { "position": 2, "team": { "id": 1783, "name": "CR Flamengo", "shortName": "Flamengo", "tla": "FLA" },
                "playedGames": 36, "form": null, "won": 20, "draw": 9, "lost": 7, "points": 69,
                "goalsFor": 60, "goalsAgainst": 30, "goalDifference": 30 },
              { "position": 1, "team": { "id": 1767, "name": "Grêmio FBPA", "shortName": "Grêmio", "tla": "GRE" },
                "playedGames": 36, "form": "W,W,D,W,W", "won": 22, "draw": 7, "lost": 7, "points": 73,
                "goalsFor": 61, "goalsAgainst": 28, "goalDifference": 33 }
            ] }
          ]
        }
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Le_a_tabela_geral_em_ordem_de_posicao_e_manda_o_token()
    {
        var handler = new QueueHandler(Ok(StandingsJson));
        var feed = Feed(handler, out _);

        var standings = await feed.GetStandingsAsync();

        Assert.Equal("https://api.football-data.org/v4/competitions/BSA/standings", handler.Uris.Single());
        Assert.Equal("token-de-teste", handler.Tokens.Single());
        Assert.Equal("2026", standings.Season);
        Assert.Equal(36, standings.CurrentMatchday);
        Assert.Equal(["Grêmio", "Flamengo"], standings.Table.Select(r => r.Team.ShortName));
        Assert.Equal(73, standings.Table[0].Points);
    }

    [Fact]
    public async Task Repete_a_resposta_do_cache_dentro_da_janela()
    {
        var handler = new QueueHandler(Ok(StandingsJson), Ok(StandingsJson));
        var feed = Feed(handler, out var clock);

        await feed.GetStandingsAsync();
        clock.UtcNow = Now.AddMinutes(9);
        await feed.GetStandingsAsync();

        Assert.Single(handler.Uris);

        clock.UtcNow = Now.AddMinutes(11);
        await feed.GetStandingsAsync();

        Assert.Equal(2, handler.Uris.Count);
    }

    [Fact]
    public async Task Provedor_fora_do_ar_serve_a_ultima_copia_boa()
    {
        var handler = new QueueHandler(Ok(StandingsJson), new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var feed = Feed(handler, out var clock);

        await feed.GetStandingsAsync();
        clock.UtcNow = Now.AddMinutes(30);

        var standings = await feed.GetStandingsAsync();

        Assert.Equal(2, standings.Table.Count);
    }

    [Fact]
    public async Task Provedor_fora_do_ar_sem_copia_vira_erro_conhecido()
    {
        var feed = Feed(new QueueHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)), out _);

        var error = await Assert.ThrowsAsync<FootballFeedUnavailableException>(() => feed.GetStandingsAsync());
        Assert.DoesNotContain("recusou", error.Message);
    }

    [Fact]
    public async Task Chave_recusada_diz_que_o_problema_e_a_chave()
    {
        // É o que acontece enquanto o segredo do Key Vault guarda o valor provisório.
        var feed = Feed(new QueueHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)), out _, apiKey: "pendente");

        var error = await Assert.ThrowsAsync<FootballFeedUnavailableException>(() => feed.GetStandingsAsync());

        Assert.Contains("recusou a chave", error.Message);
    }

    [Fact]
    public async Task Sem_chave_nao_chama_o_provedor()
    {
        var handler = new QueueHandler();
        var feed = Feed(handler, out _, apiKey: "");

        Assert.False(feed.IsConfigured);
        await Assert.ThrowsAsync<FootballFeedUnavailableException>(() => feed.GetStandingsAsync());
        Assert.Empty(handler.Uris);
    }

    [Fact]
    public void Jogos_do_time_separam_ultimos_encerrados_e_proximos_em_ordem()
    {
        var gremio = new FootballTeam(Gremio, "Grêmio FBPA", "Grêmio", "GRE");
        var inter = new FootballTeam(1766, "SC Internacional", "Internacional", "INT");
        var other = new FootballTeam(1, "Outro", "Outro", "OUT");

        FootballMatch Match(int day, string status, FootballTeam home, FootballTeam away) =>
            new(day, Now.AddDays(day), status, null, home, away, status == "FINISHED" ? 1 : null, status == "FINISHED" ? 0 : null);

        var matches = new List<FootballMatch>();
        for (var day = -10; day <= -1; day++) matches.Add(Match(day, "FINISHED", gremio, other));
        matches.Add(Match(0, "IN_PLAY", inter, gremio));
        for (var day = 1; day <= 8; day++) matches.Add(Match(day, "TIMED", other, gremio));
        matches.Add(Match(2, "TIMED", inter, other));

        var result = FootballDataFeed.ForTeam(matches, Gremio)!;

        Assert.Equal("Grêmio", result.Team.ShortName);
        Assert.Equal([-1, -2, -3, -4, -5], result.Recent.Select(m => (int)m.Id));
        Assert.Equal([0, 1, 2, 3, 4], result.Upcoming.Select(m => (int)m.Id));
        Assert.Null(FootballDataFeed.ForTeam(matches, 999));
    }

    private static FootballDataFeed Feed(QueueHandler handler, out FixedClock clock, string apiKey = "token-de-teste")
    {
        clock = new FixedClock(Now);
        var options = new FootballOptions { ApiKey = apiKey };

        return new FootballDataFeed(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            new StaticOptions(options),
            clock,
            NullLogger<FootballDataFeed>.Instance);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StaticOptions(FootballOptions value) : IOptionsMonitor<FootballOptions>
    {
        public FootballOptions CurrentValue => value;

        public FootballOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<FootballOptions, string?> listener) => null;
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> Uris { get; } = [];

        public List<string?> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.AbsoluteUri);
            Tokens.Add(request.Headers.TryGetValues("X-Auth-Token", out var values) ? values.Single() : null);

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
