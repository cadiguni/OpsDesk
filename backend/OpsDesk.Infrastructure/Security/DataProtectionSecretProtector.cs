using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Security;

/// <summary>Cifra de credencial pelo Data Protection do ASP.NET.</summary>
public class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // O propósito isola esta cifra de qualquer outro uso do Data Protection: um valor
    // cifrado aqui não é decifrável como cookie, nem o contrário.
    private readonly IDataProtector _protector = provider.CreateProtector("OpsDesk.Credentials.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? TryUnprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            // Chave desconhecida: o anel de chaves mudou desde que o valor foi cifrado.
            return null;
        }
    }
}
