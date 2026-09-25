namespace OpsDesk.Domain.Enums;

/// <summary>O que aconteceu no chamado para gerar a notificação. Persistido como string.</summary>
public enum NotificationKind
{
    /// <summary>O chamado passou a ser seu.</summary>
    Assigned,

    /// <summary>O chamado deixou de ser seu — foi repassado ou ficou sem responsável.</summary>
    Unassigned,

    /// <summary>O solicitante respondeu.</summary>
    RequesterReplied,

    /// <summary>Outra pessoa da equipe comentou, pública ou internamente.</summary>
    CommentAdded,

    /// <summary>O status mudou por ação de outra pessoa.</summary>
    StatusChanged,

    /// <summary>O chamado voltou de resolvido ou fechado para atendimento.</summary>
    Reopened
}
