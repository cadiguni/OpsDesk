namespace OpsDesk.Application.Auth;

/// <summary>
/// Nomes das claims do token de acesso.
///
/// Usamos os nomes curtos e padronizados do JWT (<c>sub</c>, <c>name</c>, <c>email</c>,
/// <c>role</c>) em vez das URIs longas do <c>ClaimTypes</c>. Motivo: o handler do
/// ASP.NET Core faz, por padrão, um remapeamento automático entre os dois formatos na
/// entrada e na saída, e esse remapeamento é a origem clássica de "a claim está no token
/// mas o RequireRole não vê". Fixando os nomes aqui e desligando o remapeamento, o que
/// entra no token é exatamente o que a autorização lê.
/// </summary>
public static class OpsDeskClaims
{
    public const string Subject = "sub";

    public const string Name = "name";

    public const string Email = "email";

    public const string Role = "role";

    /// <summary>
    /// Presente, e valendo <c>"true"</c>, enquanto a senha for provisória.
    ///
    /// Vive no token para que a trava não custe uma consulta ao banco por requisição. O
    /// atraso que isso introduz é limitado pela vida do token de acesso — quinze minutos,
    /// e na prática nem isso, porque a troca reemite a sessão na mesma resposta.
    /// </summary>
    public const string MustChangePassword = "must_change_password";
}
