using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Caixa de e-mail monitorada: o que chega nela vira chamado (docs/integracao-email.md).
///
/// Mais de uma pode existir — uma por departamento, por exemplo. A leitura é pelo Microsoft
/// Graph, com as mesmas credenciais do envio (Configurações → E-mail): um aplicativo só no
/// Entra ID, com <c>Mail.Send</c> e <c>Mail.ReadWrite</c> restritos às caixas de suporte.
/// Por isso não há credencial aqui.
/// </summary>
public class EmailAccount : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Nome para a equipe, como "Suporte TI".</summary>
    public string Name { get; set; } = null!;

    /// <summary>Endereço da caixa, em minúsculas.</summary>
    public string Address { get; set; } = null!;

    /// <summary>Categoria do chamado aberto por e-mail. Nula usa "Outros".</summary>
    public Guid? DefaultCategoryId { get; set; }

    public Category? DefaultCategory { get; set; }

    /// <summary>
    /// Prioridade inicial. Nunca vem do conteúdo da mensagem: quem escreve "URGENTE" no
    /// assunto não decide a fila. A triagem é da equipe.
    /// </summary>
    public TicketPriority DefaultPriority { get; set; } = TicketPriority.Medium;

    /// <summary>
    /// Só aceita remetente dos domínios da organização (Configurações → Geral). Mensagem de
    /// fora vai para o SPAM, e não some: a equipe pode liberar.
    /// </summary>
    public bool InternalDomainsOnly { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>Última falha de leitura, para a tela de configuração. Nula quando a última leitura deu certo.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
