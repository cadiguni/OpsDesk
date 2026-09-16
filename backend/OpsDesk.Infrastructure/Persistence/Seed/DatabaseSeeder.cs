using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Infrastructure.Persistence.Seed;

/// <summary>
/// Popula os dados de referência do sistema.
///
/// Categorias, políticas de SLA e feriados são dados que a aplicação não funciona sem, e
/// entram em qualquer ambiente. Usuários de exemplo entram apenas em desenvolvimento, por
/// motivo óbvio: são credenciais conhecidas.
///
/// O método é idempotente — roda a cada subida da API sem duplicar nada.
/// </summary>
public class DatabaseSeeder(
    OpsDeskDbContext db,
    IPasswordHasher<User> passwordHasher,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>Senha dos usuários de exemplo. Vale apenas em desenvolvimento.</summary>
    public const string DevelopmentPassword = "OpsDesk@123";

    private static readonly (string Name, string Description)[] Categories =
    [
        ("Acesso", "Permissões, contas e bloqueios de acesso"),
        ("Hardware", "Equipamentos, periféricos e manutenção física"),
        ("Software", "Instalação, licença e erro de aplicativo"),
        ("Rede", "Conectividade, Wi-Fi e cabeamento"),
        ("VPN", "Acesso remoto à rede corporativa"),
        ("E-mail", "Caixa postal, listas e entrega de mensagens"),
        ("Impressora", "Fila de impressão, driver e suprimentos"),
        ("Sistemas internos", "Aplicações desenvolvidas pela própria empresa"),
        ("Banco de dados", "Consulta, desempenho e restauração de dados"),
        ("Servidores", "Infraestrutura de servidores e serviços"),
        ("Cloud/Azure", "Recursos em nuvem e assinaturas"),
        ("Outros", "Assuntos que não se encaixam nas demais categorias")
    ];

    /// <summary>
    /// Prazos da seção 8 do README, sempre em horas úteis. O README expressa a resolução
    /// em dias úteis, e um dia útil é o expediente inteiro: 08:00 às 18:00, dez horas.
    /// </summary>
    private static readonly (TicketPriority Priority, int ResponseHours, int ResolutionHours)[] SlaPolicies =
    [
        (TicketPriority.Low, 24, 50),     // 5 dias úteis
        (TicketPriority.Medium, 8, 30),   // 3 dias úteis
        (TicketPriority.High, 4, 10),     // 1 dia útil
        (TicketPriority.Critical, 1, 4)
    ];

    public async Task SeedAsync(bool includeDevelopmentUsers, CancellationToken cancellationToken = default)
    {
        await SeedCategoriesAsync(cancellationToken);
        await SeedSlaPoliciesAsync(cancellationToken);
        await SeedHolidaysAsync(cancellationToken);

        if (includeDevelopmentUsers)
        {
            await SeedDevelopmentUsersAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Categories
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        var missing = Categories
            .Where(c => !existing.Contains(c.Name))
            .Select(c => new Category { Name = c.Name, Description = c.Description })
            .ToList();

        if (missing.Count > 0)
        {
            db.Categories.AddRange(missing);
            logger.LogInformation("Seed: {Count} categorias adicionadas.", missing.Count);
        }
    }

    private async Task SeedSlaPoliciesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.SlaPolicies
            .Where(p => p.IsActive)
            .Select(p => p.Priority)
            .ToListAsync(cancellationToken);

        var missing = SlaPolicies
            .Where(p => !existing.Contains(p.Priority))
            .Select(p => new SlaPolicy
            {
                Priority = p.Priority,
                ResponseHours = p.ResponseHours,
                ResolutionHours = p.ResolutionHours
            })
            .ToList();

        if (missing.Count > 0)
        {
            db.SlaPolicies.AddRange(missing);
            logger.LogInformation("Seed: {Count} políticas de SLA adicionadas.", missing.Count);
        }
    }

    private async Task SeedHolidaysAsync(CancellationToken cancellationToken)
    {
        // O ano corrente e os dois seguintes. Prazo de chamado não olha mais longe que isso,
        // e o seed roda a cada subida, então a janela anda sozinha.
        var currentYear = DateTime.UtcNow.Year;
        var candidates = Enumerable.Range(currentYear, 3)
            .SelectMany(BrazilianHolidays.ForYear)
            .ToList();

        var existing = await db.Holidays
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        var missing = candidates
            .Where(h => !existing.Contains(h.Date))
            .GroupBy(h => h.Date)
            .Select(g => g.First())
            .ToList();

        if (missing.Count > 0)
        {
            db.Holidays.AddRange(missing);
            logger.LogInformation("Seed: {Count} feriados adicionados.", missing.Count);
        }
    }

    private async Task SeedDevelopmentUsersAsync(CancellationToken cancellationToken)
    {
        (string Email, string Name, UserRole Role)[] users =
        [
            ("gestor@opsdesk.local", "Gestora de Suporte", UserRole.Manager),
            ("tecnico@opsdesk.local", "Técnico de Suporte", UserRole.Technician),
            ("usuario@opsdesk.local", "Usuário de Exemplo", UserRole.Requester)
        ];

        var existing = await db.Users
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        foreach (var (email, name, role) in users.Where(u => !existing.Contains(u.Email)))
        {
            var user = new User { Email = email, Name = name, Role = role };

            // Invariante 8 do CLAUDE.md: o hash sai do PasswordHasher, nunca de código próprio.
            user.PasswordHash = passwordHasher.HashPassword(user, DevelopmentPassword);

            db.Users.Add(user);
            logger.LogInformation("Seed: usuário de desenvolvimento {Email} ({Role}).", email, role);
        }
    }
}
