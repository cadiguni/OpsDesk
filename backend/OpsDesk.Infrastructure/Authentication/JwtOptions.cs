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

    /// <summary>
    /// Janela em que um refresh token já rotacionado continua sendo aceito.
    ///
    /// Sem ela, a rotação de uso único é estrita demais para o mundo real: duas abas do
    /// sistema recarregando ao mesmo tempo, uma requisição repetida por queda de rede, ou
    /// o efeito executado duas vezes pelo StrictMode do React produzem duas renovações
    /// concorrentes com o mesmo token. A segunda pareceria reuso e derrubaria todas as
    /// sessões do usuário — um logout aparentemente aleatório, provocado pelo próprio
    /// cliente legítimo.
    ///
    /// A tolerância é curta de propósito: fora dela, apresentar um token rotacionado
    /// continua sendo tratado como vazamento. É o mesmo desenho que a especificação de
    /// boas práticas do OAuth 2.0 chama de <i>leeway</i> na rotação.
    /// </summary>
    [Range(0, 300)]
    public int RefreshTokenGraceSeconds { get; set; } = 30;
}
