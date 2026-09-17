using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Tickets;

// ----- Comentários -----

/// <summary>
/// Novo comentário.
///
/// <paramref name="IsInternal"/> vem do cliente, mas não é aceito de qualquer um: só
/// técnico e gestor podem marcar um comentário como interno. Solicitante pedindo comentário
/// interno é recusado em vez de silenciosamente convertido em público — converter esconderia
/// um erro de cliente e publicaria para o solicitante um texto que alguém quis restringir.
/// </summary>
public record AddCommentRequest(
    string Content,
    bool IsInternal = false,
    bool CloseTicket = false,
    Guid[]? AttachmentIds = null);

public record TicketCommentItem(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    UserRole AuthorRole,
    string Content,
    bool IsInternal,
    DateTimeOffset CreatedAt);

public abstract record AddCommentResult
{
    public sealed record ClosingNotAllowed : AddCommentResult;

    public sealed record Added(TicketCommentItem Comment) : AddCommentResult;

    /// <summary>Chamado inexistente ou fora da visibilidade de quem pediu.</summary>
    public sealed record TicketNotFound : AddCommentResult;

    public sealed record InternalNotAllowed : AddCommentResult
    {
        public string Message => "Apenas técnicos e gestores podem registrar comentário interno.";
    }

    public sealed record TicketClosed : AddCommentResult
    {
        public string Message => "Este chamado está encerrado e não aceita novos comentários.";
    }

    public sealed record AttachmentsInvalid : AddCommentResult
    {
        public string Message =>
            "Algum anexo não foi encontrado ou já pertence a outro chamado. O comentário não foi enviado.";
    }
}

// ----- Mudança de status -----

public record ChangeStatusRequest(TicketStatus Status);

public abstract record ChangeStatusResult
{
    public sealed record Changed(TicketDetail Ticket) : ChangeStatusResult;

    public sealed record TicketNotFound : ChangeStatusResult;

    public sealed record TransitionRejected(TicketStatus From, TicketStatus To) : ChangeStatusResult
    {
        public string Message =>
            $"Não é possível mudar de {From} para {To}, ou seu perfil não permite essa mudança.";
    }
}

// ----- Atribuição -----

/// <summary>
/// Atribuição de responsável. <c>null</c> remove o responsável.
/// </summary>
public record AssignRequest(Guid? TechnicianId);

public abstract record AssignResult
{
    public sealed record Assigned(TicketDetail Ticket) : AssignResult;

    /// <summary>
    /// A atribuição foi gravada, mas o chamado saiu da visibilidade de quem atribuiu —
    /// caso do técnico que encaminha para um colega.
    /// </summary>
    public sealed record AssignedAndHidden : AssignResult;

    public sealed record TicketNotFound : AssignResult;

    public sealed record NotAllowed(string Message) : AssignResult;

    public sealed record TechnicianInvalid : AssignResult
    {
        public string Message => "O responsável precisa ser um técnico ou gestor ativo.";
    }
}

// ----- Classificação: prioridade e categoria -----

/// <summary>
/// Reclassificação do chamado (README, seção 6.1). É o trabalho da triagem.
///
/// Os dois campos são opcionais e independentes: mexer só na prioridade, só na categoria,
/// ou nas duas. Nulo quer dizer "não mexa", e não "limpe" — chamado sem categoria ou sem
/// prioridade não existe no domínio.
/// </summary>
public record ChangeClassificationRequest(TicketPriority? Priority, Guid? CategoryId);

public abstract record ChangeClassificationResult
{
    public sealed record Changed(TicketDetail Ticket) : ChangeClassificationResult;

    public sealed record TicketNotFound : ChangeClassificationResult;

    public sealed record NotAllowed : ChangeClassificationResult
    {
        public string Message => "Apenas técnicos e gestores reclassificam chamado.";
    }

    public sealed record CategoryNotFound : ChangeClassificationResult
    {
        public string Message => "Categoria inválida ou inativa.";
    }

    public sealed record TicketClosed : ChangeClassificationResult
    {
        public string Message =>
            "Chamado encerrado não é reclassificado: mudar a prioridade agora alteraria indicador de um atendimento já concluído.";
    }

    public sealed record SlaPolicyMissing(TicketPriority Priority) : ChangeClassificationResult
    {
        public string Message => $"Não há política de SLA ativa para a prioridade {Priority}.";
    }
}

// ----- Histórico -----

public record TicketHistoryItem(
    Guid Id,
    TicketHistoryAction Action,
    string? PreviousValue,
    string? NewValue,
    Guid? ChangedById,
    string? ChangedByName,
    DateTimeOffset CreatedAt);

/// <summary>Técnico ou gestor disponível para receber chamado.</summary>
public record StaffOption(Guid Id, string Name, UserRole Role);
