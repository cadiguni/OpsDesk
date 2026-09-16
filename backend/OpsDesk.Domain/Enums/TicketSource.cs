namespace OpsDesk.Domain.Enums;

/// <summary>Origem do chamado. Persistida como string. Na versão 1 vale sempre <see cref="Portal"/>.</summary>
public enum TicketSource
{
    Portal,
    Email,
    Csv,
    Freshdesk,
    Freshservice,
    Glpi,
    ExternalApi
}
