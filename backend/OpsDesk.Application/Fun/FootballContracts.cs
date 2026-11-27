namespace OpsDesk.Application.Fun;

// A "arquibancada" é um cantinho de descontração, de propósito fora da navegação e da
// documentação de produto: a tabela do Brasileirão e os jogos de um time. Nada aqui toca
// chamado, usuário ou SLA.

public record FootballTeam(int Id, string Name, string ShortName, string Tla);

public record StandingRow(
    int Position,
    FootballTeam Team,
    int Played,
    int Won,
    int Draw,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    string? Form);

public record Standings(string Competition, string? Season, int? CurrentMatchday, IReadOnlyList<StandingRow> Table);

public record FootballMatch(
    long Id,
    DateTimeOffset UtcDate,
    string Status,
    int? Matchday,
    FootballTeam Home,
    FootballTeam Away,
    int? HomeGoals,
    int? AwayGoals);

public record TeamMatches(FootballTeam Team, IReadOnlyList<FootballMatch> Recent, IReadOnlyList<FootballMatch> Upcoming);

/// <summary>Fonte dos dados de futebol. Sem chave configurada, <see cref="IsConfigured"/> é falso.</summary>
public interface IFootballFeed
{
    bool IsConfigured { get; }

    Task<Standings> GetStandingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Últimos jogos e próximos jogos do time. Nulo quando o time não joga a competição.</summary>
    Task<TeamMatches?> GetTeamMatchesAsync(int teamId, CancellationToken cancellationToken = default);
}

/// <summary>O provedor não respondeu e não há cópia anterior para mostrar no lugar.</summary>
public class FootballFeedUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
