using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Configuração do envio de e-mail pelo Microsoft Graph, editada pelo gestor no portal.
///
/// Linha única (<see cref="SingletonId"/>). Mora no banco, e não em variável de ambiente,
/// porque o client secret do Entra ID expira e precisa ser trocado sem deploy.
/// </summary>
public class EmailSettings : IHasUpdatedAt
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool IsEnabled { get; set; }

    /// <summary>Caixa que envia — a de suporte, que a versão 2.0 também vai ler.</summary>
    public string? SenderAddress { get; set; }

    public string? SenderName { get; set; }

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    /// <summary>
    /// Client secret cifrado pelo Data Protection. Nunca sai da API: a tela só sabe se
    /// existe e quando foi trocado.
    /// </summary>
    public string? ProtectedClientSecret { get; set; }

    public DateTimeOffset? ClientSecretUpdatedAt { get; set; }

    /// <summary>Validade informada pelo gestor, como o Entra ID a mostra. Serve ao aviso de vencimento.</summary>
    public DateOnly? ClientSecretExpiresOn { get; set; }

    /// <summary>Endereço do portal, para o link do e-mail até o chamado.</summary>
    public string? PortalUrl { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastSucceededAt { get; set; }

    public string? LastError { get; set; }

    public Guid? UpdatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
