using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Fun;

namespace OpsDesk.Infrastructure.Fun;

/// <summary>Configuração da fonte de dados da arquibancada (seção <c>Football</c>).</summary>
public class FootballOptions
{
    public const string SectionName = "Football";

    /// <summary>Token gratuito da football-data.org. Vazio desliga a tabela, sem erro na subida.</summary>
    public string? ApiKey { get; set; }

    [Required]
    public string BaseUrl { get; set; } = "https://api.football-data.org/v4/";

    /// <summary>Código da competição na football-data.org. <c>BSA</c> é a Série A do Brasileirão.</summary>
    [Required]
    public string Competition { get; set; } = "BSA";

    /// <summary>
    /// Quanto tempo uma resposta vale antes de perguntar de novo. O plano gratuito aceita
    /// dez chamadas por minuto; com cache, a conta é por instalação e não por pessoa.
    /// </summary>
    [Range(1, 1440)]
    public int CacheMinutes { get; set; } = 10;
}

/// <summary>
/// Lê a tabela e os jogos da competição na football-data.org.
///
/// Os jogos vêm numa chamada só, da temporada inteira, e o filtro por time é feito aqui:
/// assim trocar de time na tela não gasta cota. Se o provedor falhar, devolve a última
/// cópia boa (até seis horas), porque tabela de algumas horas atrás é melhor que erro.
/// </summary>
public class FootballDataFeed(
    HttpClient http,
    IMemoryCache cache,
    IOptionsMonitor<FootballOptions> options,
    IClock clock,
    ILogger<FootballDataFeed> logger) : IFootballFeed
{
    private static readonly TimeSpan StaleLimit = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Status que ainda vão acontecer, ou estão acontecendo.</summary>
    private static readonly HashSet<string> Pending = ["SCHEDULED", "TIMED", "IN_PLAY", "PAUSED", "POSTPONED", "SUSPENDED"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.ApiKey);

    public async Task<Standings> GetStandingsAsync(CancellationToken cancellationToken = default)
    {
        var competition = options.CurrentValue.Competition;

        var response = await FetchAsync<StandingsResponse>(
            $"competitions/{competition}/standings", cancellationToken);

        var total = response.Standings?.FirstOrDefault(s => s.Type == "TOTAL") ?? response.Standings?.FirstOrDefault();

        var table = (total?.Table ?? [])
            .Where(row => row.Team?.Id is not null)
            .Select(row => new StandingRow(
                row.Position,
                ToTeam(row.Team!)!,
                row.PlayedGames,
                row.Won,
                row.Draw,
                row.Lost,
                row.GoalsFor,
                row.GoalsAgainst,
                row.GoalDifference,
                row.Points,
                row.Form))
            .OrderBy(row => row.Position)
            .ToList();

        return new Standings(
            response.Competition?.Name ?? competition,
            response.Season?.StartDate is { Length: >= 4 } start ? start[..4] : null,
            response.Season?.CurrentMatchday,
            table);
    }

    public async Task<TeamMatches?> GetTeamMatchesAsync(int teamId, CancellationToken cancellationToken = default)
    {
        var response = await FetchAsync<MatchesResponse>(
            $"competitions/{options.CurrentValue.Competition}/matches", cancellationToken);

        var matches = (response.Matches ?? [])
            .Select(ToMatch)
            .OfType<FootballMatch>()
            .ToList();

        return ForTeam(matches, teamId);
    }

    /// <summary>Os cinco últimos jogos encerrados e os cinco próximos do time.</summary>
    public static TeamMatches? ForTeam(IReadOnlyCollection<FootballMatch> matches, int teamId, int take = 5)
    {
        var played = matches.Where(m => m.Home.Id == teamId || m.Away.Id == teamId).ToList();

        if (played.Count == 0)
        {
            return null;
        }

        var team = played[0].Home.Id == teamId ? played[0].Home : played[0].Away;

        var recent = played
            .Where(m => m.Status == "FINISHED")
            .OrderByDescending(m => m.UtcDate)
            .Take(take)
            .ToList();

        var upcoming = played
            .Where(m => Pending.Contains(m.Status))
            .OrderBy(m => m.UtcDate)
            .Take(take)
            .ToList();

        return new TeamMatches(team, recent, upcoming);
    }

    private async Task<T> FetchAsync<T>(string path, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new FootballFeedUnavailableException("Fonte de dados de futebol não configurada.");
        }

        var key = $"football:{path}";
        var cached = cache.Get<CachedResponse<T>>(key);

        if (cached is not null && clock.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(settings.CacheMinutes))
        {
            return cached.Value;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl), path));
            request.Headers.Add("X-Auth-Token", settings.ApiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                        ?? throw new JsonException("Resposta vazia.");

            cache.Set(key, new CachedResponse<T>(value, clock.UtcNow), StaleLimit);

            return value;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            if (cached is not null)
            {
                logger.LogWarning(exception, "football-data.org falhou em {Path}; servindo a cópia de {FetchedAt}.",
                    path, cached.FetchedAt);
                return cached.Value;
            }

            logger.LogWarning(exception, "football-data.org falhou em {Path}, sem cópia anterior.", path);
            throw new FootballFeedUnavailableException("Não foi possível consultar os dados de futebol agora.", exception);
        }
    }

    private static FootballTeam? ToTeam(TeamDto team) =>
        team.Id is not { } id
            ? null
            : new FootballTeam(id, team.Name ?? "?", team.ShortName ?? team.Name ?? "?", team.Tla ?? "");

    private static FootballMatch? ToMatch(MatchDto match)
    {
        // Jogo de mata-mata ainda sem adversário definido vem com time nulo.
        if (match.HomeTeam is null || match.AwayTeam is null
            || ToTeam(match.HomeTeam) is not { } home || ToTeam(match.AwayTeam) is not { } away)
        {
            return null;
        }

        return new FootballMatch(
            match.Id,
            match.UtcDate,
            match.Status ?? "SCHEDULED",
            match.Matchday,
            home,
            away,
            match.Score?.FullTime?.Home,
            match.Score?.FullTime?.Away);
    }

    private sealed record CachedResponse<T>(T Value, DateTimeOffset FetchedAt);

    // ----- Formato da football-data.org (v4), só o que é lido -----

    private sealed record TeamDto(int? Id, string? Name, string? ShortName, string? Tla);

    private sealed record CompetitionDto(string? Name);

    private sealed record SeasonDto(string? StartDate, int? CurrentMatchday);

    private sealed record TableRowDto(
        int Position, TeamDto? Team, int PlayedGames, string? Form, int Won, int Draw, int Lost,
        int Points, int GoalsFor, int GoalsAgainst, int GoalDifference);

    private sealed record StandingDto(string? Type, List<TableRowDto>? Table);

    private sealed record StandingsResponse(CompetitionDto? Competition, SeasonDto? Season, List<StandingDto>? Standings);

    private sealed record ScoreLineDto(int? Home, int? Away);

    private sealed record ScoreDto(ScoreLineDto? FullTime);

    private sealed record MatchDto(
        long Id, DateTimeOffset UtcDate, string? Status, int? Matchday, TeamDto? HomeTeam, TeamDto? AwayTeam, ScoreDto? Score);

    private sealed record MatchesResponse(List<MatchDto>? Matches);
}
