using System.ComponentModel.DataAnnotations;

namespace OpsDesk.Infrastructure.Authentication;

/// <summary>
/// Parâmetros do JWT de acesso e do refresh token (decisão 4.6 de docs/arquitetura.md).
///
/// A chave não tem valor padrão de propósito: um padrão em código viraria a chave de
/// produção no dia em que alguém esquecesse de configurar a variável de ambiente.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "opsdesk";

    [Required]
    public string Audience { get; set; } = "opsdesk";

    /// <summary>Chave de assinatura HMAC. Vem de variável de ambiente ou cofre, nunca do repositório.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "A chave de assinatura do JWT precisa de ao menos 32 caracteres.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Validade do token de acesso. Curta: o refresh token é quem sustenta a sessão.</summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;
}
