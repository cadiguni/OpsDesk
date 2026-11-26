using OpsDesk.Application.Common;

namespace OpsDesk.Application.JobTitles;

/// <summary>Filtro da listagem de administração. Nulo quer dizer "qualquer".</summary>
public record ManagedJobTitleFilter(string? Search, bool? IsActive)
{
    public PageRequest Page { get; init; } = new();
}

/// <summary>Cargo visto pelo administrador, com quantas pessoas o têm.</summary>
public record ManagedJobTitle(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, int Users);

/// <summary>Cargo ativo, para o seletor do cadastro de usuário.</summary>
public record JobTitleOption(Guid Id, string Name);

public record SaveJobTitleRequest(string Name);

public record ChangeJobTitleActivationRequest(bool IsActive);

public abstract record SaveJobTitleResult
{
    public sealed record Saved(ManagedJobTitle JobTitle) : SaveJobTitleResult;

    public sealed record JobTitleNotFound : SaveJobTitleResult;

    /// <summary>Mesmo critério das categorias: sem diferenciar maiúsculas, desativado conta.</summary>
    public sealed record NameTaken : SaveJobTitleResult
    {
        public string Message => "Já existe um cargo com esse nome. Reative-o ou escolha outro nome.";
    }
}
