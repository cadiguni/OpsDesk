using System.ComponentModel.DataAnnotations;
using OpsDesk.Application.Auth;

namespace OpsDesk.Infrastructure.Persistence.Seed;

/// <summary>
/// Credencial do primeiro gestor, para instalação em branco.
///
/// Sem gestor não há quem atribua, reclassifique ou administre, e o cadastro pela tela
/// sempre cria perfil Usuário: numa instalação nova não existe caminho pela interface
/// para o primeiro gestor existir. Isto resolve o impasse pela configuração —
/// <c>OpsDesk__Bootstrap__Email</c> e <c>OpsDesk__Bootstrap__Password</c> no ambiente do
/// contêiner, ou no cofre de segredos do orquestrador.
///
/// A alternativa de "o primeiro usuário cadastrado vira gestor" foi descartada: numa
/// instância exposta antes de ser configurada, quem chegasse primeiro viraria
/// administrador do sistema.
///
/// O preço deste caminho é a senha existir em configuração, que é lugar que vaza — em log
/// de deploy, em dump de variável de ambiente, no histórico do shell. Por isso o usuário
/// criado aqui nasce com <see cref="Domain.Entities.User.MustChangePassword"/>, e a
/// credencial vale exatamente uma vez.
/// </summary>
public class BootstrapAdminOptions
{
    public const string SectionName = "OpsDesk:Bootstrap";

    /// <summary>
    /// E-mail do primeiro gestor. Vazio desliga o bootstrap: ambiente sem esta variável
    /// simplesmente não cria ninguém, e o comando registra o motivo no log.
    /// </summary>
    [EmailAddress(ErrorMessage = "OpsDesk:Bootstrap:Email não é um e-mail válido.")]
    [MaxLength(320)]
    public string? Email { get; set; }

    /// <summary>Senha inicial, obrigatoriamente trocada no primeiro acesso.</summary>
    [MinLength(PasswordPolicy.MinimumLength,
        ErrorMessage = "OpsDesk:Bootstrap:Password deve ter no mínimo 8 caracteres.")]
    [MaxLength(PasswordPolicy.MaximumLength)]
    public string? Password { get; set; }

    /// <summary>Nome exibido. O padrão serve, e a pessoa corrige depois pelo perfil.</summary>
    [MaxLength(200)]
    public string Name { get; set; } = "Gestor";

    /// <summary>Configuração preenchida o bastante para tentar criar o gestor.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
