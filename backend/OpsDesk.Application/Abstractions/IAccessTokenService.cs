using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Emissão do token de acesso. A camada de aplicação não conhece JWT, chave de assinatura
/// nem formato de claim — só pede um token para um usuário.
/// </summary>
public interface IAccessTokenService
{
    AccessToken Create(User user);
}

/// <summary>Token de acesso e o instante em que ele deixa de valer.</summary>
public readonly record struct AccessToken(string Value, DateTimeOffset ExpiresAt);
