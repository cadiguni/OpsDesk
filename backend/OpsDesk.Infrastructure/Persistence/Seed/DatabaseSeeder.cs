using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    IOptions<BootstrapAdminOptions> bootstrapOptions,
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

    /// <summary>
    /// Cria o primeiro gestor a partir de <see cref="BootstrapAdminOptions"/>.
    ///
    /// Roda uma única vez, no sentido que importa: havendo qualquer gestor no banco, não
    /// faz nada. É o que impede a variável de ambiente esquecida no orquestrador de
    /// ressuscitar uma conta administrativa a cada deploy — inclusive depois de alguém a
    /// ter desativado de propósito, que é o caso perigoso.
    ///
    /// Quando o e-mail já pertence a alguém, promovemos essa conta em vez de tentar criar
    /// outra: é o que a pessoa que se cadastrou pelo portal e agora precisa administrar o
    /// sistema espera, e evita colidir no índice único. Nesse caminho a senha
    /// configurada é ignorada — a pessoa já tem a dela.
    /// </summary>
    public async Task<BootstrapAdminResult> EnsureBootstrapAdminAsync(
        CancellationToken cancellationToken = default)
    {
        var options = bootstrapOptions.Value;

        if (!options.IsConfigured)
        {
            logger.LogInformation(
                "Bootstrap: {Section}:Email e {Section}:Password não configurados, nada a fazer.",
                BootstrapAdminOptions.SectionName, BootstrapAdminOptions.SectionName);

            return BootstrapAdminResult.NotConfigured;
        }

        if (await db.Users.AnyAsync(u => u.Role == UserRole.Manager, cancellationToken))
        {
            logger.LogInformation("Bootstrap: já existe gestor no sistema, nada a fazer.");

            return BootstrapAdminResult.ManagerAlreadyExists;
        }

        // Mesmo critério do login: e-mail é identificador, sempre em minúsculas.
        var email = options.Email!.Trim().ToLowerInvariant();

        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (existing is not null)
        {
            existing.Role = UserRole.Manager;
            existing.IsActive = true;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Bootstrap: {Email} já existia e foi promovido a gestor. A senha configurada " +
                "foi ignorada — a conta mantém a senha que já tinha.", email);

            return BootstrapAdminResult.Promoted;
        }

        var manager = new User
        {
            Name = options.Name.Trim(),
            Email = email,
            Role = UserRole.Manager,

            // A senha chegou por configuração, e configuração vaza. Vale uma vez.
            MustChangePassword = true
        };

        // Invariante 8 do CLAUDE.md: o hash sai do PasswordHasher, nunca de código próprio.
        manager.PasswordHash = passwordHasher.HashPassword(manager, options.Password!);

        db.Users.Add(manager);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Bootstrap: gestor {Email} criado. A senha configurada vale uma vez e precisa ser " +
            "trocada no primeiro acesso.", email);

        return BootstrapAdminResult.Created;
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

/// <summary>O que o bootstrap do primeiro gestor fez, para o comando relatar.</summary>
public enum BootstrapAdminResult
{
    /// <summary>Sem e-mail ou sem senha em configuração.</summary>
    NotConfigured,

    /// <summary>Já havia gestor: o bootstrap não toca em sistema já instalado.</summary>
    ManagerAlreadyExists,

    /// <summary>Conta já existente promovida a gestor, com a senha que já tinha.</summary>
    Promoted,

    /// <summary>Gestor criado, com troca de senha obrigatória no primeiro acesso.</summary>
    Created
}
