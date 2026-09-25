namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Cifra de credenciais guardadas no banco. A implementação usa o Data Protection do
/// ASP.NET, cujas chaves precisam sobreviver a um novo deploy — ver CLAUDE.md.
/// </summary>
public interface ISecretProtector
{
    string Protect(string secret);

    /// <summary>
    /// O valor em claro, ou nulo quando não dá para decifrar — o caso típico é a chave de
    /// cifragem ter mudado porque não estava em volume persistente.
    /// </summary>
    string? TryUnprotect(string protectedSecret);
}
