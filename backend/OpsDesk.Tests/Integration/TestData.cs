using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Tests.Integration;

/// <summary>Montagem de cenário para os testes de integração.</summary>
internal static class TestData
{
    /// <summary>
    /// O seeder de verdade, com as dependências que o teste não quer montar.
    ///
    /// <paramref name="bootstrap"/> ausente é o caso comum: sem e-mail nem senha, o
    /// bootstrap do primeiro gestor não faz nada, que é o que a maioria dos testes espera.
    /// </summary>
    internal static DatabaseSeeder NewSeeder(
        OpsDeskDbContext db, BootstrapAdminOptions? bootstrap = null) =>
        new(db,
            new PasswordHasher<User>(),
            Options.Create(bootstrap ?? new BootstrapAdminOptions()),
            NullLogger<DatabaseSeeder>.Instance);

    internal static User NewUser(UserRole role, string? email = null) => new()
    {
        Name = $"Usuário {role}",
        Email = email ?? $"{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@opsdesk.local",
        Role = role,
        PasswordHash = "hash-irrelevante-para-este-teste"
    };

    internal static Category NewCategory(string? name = null) => new()
    {
        Name = name ?? $"Categoria {Guid.NewGuid():N}"
    };

    internal static Ticket NewTicket(
        User requester,
        Category category,
        User? technician = null,
        TicketStatus status = TicketStatus.Open,
        TicketPriority priority = TicketPriority.Medium,
        string title = "Não consigo acessar a VPN") => new()
        {
            Title = title,
            Description = "Descrição do chamado de teste.",
            Status = status,
            Priority = priority,
            RequesterId = requester.Id,
            AssignedTechnicianId = technician?.Id,
            CategoryId = category.Id,
            Source = TicketSource.Portal,
            SlaResponseDueAt = DateTimeOffset.UtcNow.AddHours(8),
            SlaResolutionDueAt = DateTimeOffset.UtcNow.AddHours(24)
        };

    /// <summary>
    /// Cenário usado pelos testes de autorização: um solicitante, um segundo solicitante,
    /// dois técnicos e chamados cobrindo cada combinação de responsável.
    /// </summary>
    internal static async Task<Scenario> SeedScenarioAsync(OpsDeskDbContext db)
    {
        var requester = NewUser(UserRole.Requester);
        var otherRequester = NewUser(UserRole.Requester);
        var technician = NewUser(UserRole.Technician);
        var otherTechnician = NewUser(UserRole.Technician);
        var manager = NewUser(UserRole.Manager);
        var category = NewCategory();

        db.AddRange(requester, otherRequester, technician, otherTechnician, manager, category);
        await db.SaveChangesAsync();

        var own = NewTicket(requester, category, title: "Chamado do solicitante");
        var ofSomeoneElse = NewTicket(otherRequester, category, technician, title: "Chamado de terceiro");
        var unassigned = NewTicket(otherRequester, category, title: "Sem responsável");
        var ofOtherTechnician = NewTicket(otherRequester, category, otherTechnician, title: "De outro técnico");

        db.AddRange(own, ofSomeoneElse, unassigned, ofOtherTechnician);
        await db.SaveChangesAsync();

        return new Scenario(
            requester, otherRequester, technician, otherTechnician, manager, category,
            own, ofSomeoneElse, unassigned, ofOtherTechnician);
    }

    internal record Scenario(
        User Requester,
        User OtherRequester,
        User Technician,
        User OtherTechnician,
        User Manager,
        Category Category,
        Ticket Own,
        Ticket OfSomeoneElse,
        Ticket Unassigned,
        Ticket OfOtherTechnician);
}
