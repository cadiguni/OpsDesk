using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Configuração geral do sistema, editada pelo gestor (README, seção 13.12). Linha única.
/// </summary>
public class SystemSettings : IHasUpdatedAt
{
    public const int SingletonId = 1;

    public const string DefaultOrganizationName = "OpsDesk";

    public int Id { get; set; } = SingletonId;

    /// <summary>Nome exibido no portal e nos e-mails. Nulo usa o nome do produto.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>
    /// Qualquer pessoa pode criar conta pelo portal. Desligado, contas só nascem pela
    /// administração de usuários — o caso de quem não quer o portal aberto para a internet.
    /// </summary>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>
    /// Domínios aceitos no autocadastro, separados por vírgula, em minúsculas — por exemplo
    /// <c>empresa.com,empresa.com.br</c>. Vazio aceita qualquer domínio. Não restringe o que
    /// o gestor cadastra pela administração.
    /// </summary>
    public string? AllowedEmailDomains { get; set; }

    /// <summary>
    /// Botão "Entrar com Microsoft" no login. Usa o aplicativo do Entra ID já configurado para o
    /// e-mail (tenant, client id e secret), e cria a conta de solicitante no primeiro acesso de
    /// qualquer pessoa do tenant — independentemente do autocadastro, que é o cadastro com senha.
    /// </summary>
    public bool MicrosoftSignInEnabled { get; set; }

    public Guid? UpdatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
