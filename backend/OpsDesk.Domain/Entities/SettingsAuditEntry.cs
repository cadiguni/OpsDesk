using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Quem mudou uma configuração do sistema, o quê e quando. Guarda os nomes dos campos
/// alterados, nunca os valores: o valor de um deles é uma credencial.
/// </summary>
public class SettingsAuditEntry : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Área da configuração, por exemplo <c>Email</c>.</summary>
    public string Area { get; set; } = null!;

    /// <summary>Campos alterados, separados por vírgula.</summary>
    public string ChangedFields { get; set; } = null!;

    public Guid? ChangedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
