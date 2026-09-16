using Microsoft.AspNetCore.Authorization;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Authorization;

/// <summary>
/// Políticas por perfil.
///
/// Atenção ao que estas políticas são e ao que não são: elas barram o acesso ao endpoint,
/// e nada mais. Quem decide *quais* chamados uma pessoa vê é o filtro de visibilidade no
/// <c>IQueryable</c> (invariante 1 do CLAUDE.md). Política de rota não protege dado de
/// terceiro: basta um endpoint novo sem o atributo para o vazamento existir.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Técnico ou gestor. Painel técnico, comentário interno, mudança de status.</summary>
    public const string Staff = "staff";

    /// <summary>Apenas gestor. Dashboard, atribuição de responsável, gestão de categorias.</summary>
    public const string Manager = "manager";

    public static AuthorizationBuilder AddOpsDeskPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(Staff, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.Technician), nameof(UserRole.Manager)))
        .AddPolicy(Manager, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.Manager)));
}
