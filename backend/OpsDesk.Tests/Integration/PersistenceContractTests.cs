using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Application.Sla;
using OpsDesk.Infrastructure.Persistence.Seed;
using OpsDesk.Infrastructure.Time;
using OpsDesk.Tests.Unit;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Contratos que o banco tem que sustentar: enum legível, data em UTC, unicidade e seed
/// idempotente. São as invariantes 4, 7 e 8 do CLAUDE.md vistas do lado do PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PersistenceContractTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Enums_sao_gravados_como_texto_para_o_banco_continuar_legivel()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        // SqlQuery projeta escalar por uma coluna chamada Value; daí o alias.
        var stored = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM tickets WHERE id = {s.Own.Id}")
            .SingleAsync();

        Assert.Equal("Open", stored);
    }

    [Fact]
    public async Task Colunas_de_data_sao_timestamptz()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var types = await db.Database
            .SqlQuery<string>($"""
                SELECT DISTINCT data_type AS "Value" FROM information_schema.columns
                WHERE table_name = 'tickets' AND column_name LIKE '%\_at'
                """)
            .ToListAsync();

        Assert.Equal(["timestamp with time zone"], types);
    }

    [Fact]
    public async Task Data_faz_ida_e_volta_preservando_o_instante()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        // 09:00 em São Paulo, escrito em UTC, que é o que o banco aceita.
        var saoPaulo = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(-3));

        await using var write = fixture.CreateContext();
        var ticket = await write.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.FirstRespondedAt = saoPaulo.ToUniversalTime();
        await write.SaveChangesAsync();

        await using var read = fixture.CreateContext();
        var stored = await read.Tickets
            .Where(t => t.Id == s.Own.Id)
            .Select(t => t.FirstRespondedAt)
            .SingleAsync();

        Assert.Equal(saoPaulo, stored);
        Assert.Equal(TimeSpan.Zero, stored!.Value.Offset);
    }

    [Fact]
    public async Task Npgsql_recusa_data_com_deslocamento_diferente_de_utc()
    {
        // A armadilha conhecida, fixada por teste: não basta usar DateTimeOffset em vez de
        // DateTime, o deslocamento tem que ser zero. Qualquer valor com -03:00 é recusado
        // na escrita, e é por isso que IBusinessCalendar devolve prazo em UTC.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        await using var write = fixture.CreateContext();
        var ticket = await write.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.FirstRespondedAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(-3));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
        Assert.Contains("UTC", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prazo_calculado_pelo_calendario_e_gravavel_sem_conversao()
    {
        // O teste que faltava: o prazo sai do IBusinessCalendar e vai direto para a coluna.
        // Se o calendário voltar a devolver deslocamento local, isto quebra aqui e não em
        // produção, na primeira abertura de chamado.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var calendar = new BusinessCalendar(
            Options.Create(new BusinessHoursOptions()), new FixedHolidayProvider());

        var deadlines = new SlaClock(calendar).Calculate(
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            new SlaPolicy { Priority = TicketPriority.High, ResponseHours = 4, ResolutionHours = 10 });

        await using var write = fixture.CreateContext();
        var ticket = await write.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.SlaResponseDueAt = deadlines.ResponseDueAt;
        ticket.SlaResolutionDueAt = deadlines.ResolutionDueAt;

        await write.SaveChangesAsync();

        Assert.Equal(TimeSpan.Zero, deadlines.ResponseDueAt.Offset);
        Assert.Equal(TimeSpan.Zero, deadlines.ResolutionDueAt.Offset);
    }

    [Fact]
    public async Task Timestamps_sao_preenchidos_pelo_interceptor()
    {
        await fixture.ResetAsync();

        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        await using var db = fixture.CreateContext(now: now);
        var requester = TestData.NewUser(UserRole.Requester);
        var category = TestData.NewCategory();
        db.AddRange(requester, category);
        await db.SaveChangesAsync();

        var ticket = TestData.NewTicket(requester, category);
        db.Add(ticket);
        await db.SaveChangesAsync();

        Assert.Equal(now, ticket.CreatedAt);
        Assert.Equal(now, ticket.UpdatedAt);
    }

    [Fact]
    public async Task Alteracao_move_o_updated_at_e_preserva_o_created_at()
    {
        await fixture.ResetAsync();

        var created = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var changed = created.AddHours(2);

        await using var setup = fixture.CreateContext(now: created);
        var s = await TestData.SeedScenarioAsync(setup);

        await using var db = fixture.CreateContext(now: changed);
        var ticket = await db.Tickets.SingleAsync(t => t.Id == s.Own.Id);
        ticket.Status = TicketStatus.InProgress;
        await db.SaveChangesAsync();

        Assert.Equal(created, ticket.CreatedAt);
        Assert.Equal(changed, ticket.UpdatedAt);
    }

    [Fact]
    public async Task Email_repetido_e_recusado_pelo_banco()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        db.Add(TestData.NewUser(UserRole.Requester, "duplicado@opsdesk.local"));
        await db.SaveChangesAsync();

        await using var other = fixture.CreateContext();
        other.Add(TestData.NewUser(UserRole.Technician, "duplicado@opsdesk.local"));

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Duas_politicas_ativas_para_a_mesma_prioridade_sao_recusadas()
    {
        // Duas ativas fariam o prazo do chamado depender da ordem da query.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        db.Add(new SlaPolicy { Priority = TicketPriority.High, ResponseHours = 4, ResolutionHours = 10 });
        await db.SaveChangesAsync();

        await using var other = fixture.CreateContext();
        other.Add(new SlaPolicy { Priority = TicketPriority.High, ResponseHours = 2, ResolutionHours = 6 });

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Politica_inativa_nao_conflita_com_a_ativa()
    {
        // O índice é filtrado por is_active justamente para permitir manter o histórico
        // de políticas antigas desativadas.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        db.Add(new SlaPolicy
        {
            Priority = TicketPriority.High,
            ResponseHours = 8,
            ResolutionHours = 20,
            IsActive = false
        });
        db.Add(new SlaPolicy { Priority = TicketPriority.High, ResponseHours = 4, ResolutionHours = 10 });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.SlaPolicies.CountAsync(p => p.Priority == TicketPriority.High));
    }

    [Fact]
    public async Task Mesmo_email_externo_nao_abre_dois_chamados()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        var first = TestData.NewTicket(s.Requester, s.Category);
        first.Source = TicketSource.Email;
        first.ExternalId = "<mensagem-1@empresa.com>";
        db.Add(first);
        await db.SaveChangesAsync();

        await using var other = fixture.CreateContext();
        var duplicate = TestData.NewTicket(s.Requester, s.Category);
        duplicate.Source = TicketSource.Email;
        duplicate.ExternalId = "<mensagem-1@empresa.com>";
        other.Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Chamados_do_portal_nao_colidem_por_external_id_nulo()
    {
        // O índice é filtrado: sem o filtro, o segundo chamado aberto pelo portal
        // colidiria com o primeiro em (Portal, NULL).
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedScenarioAsync(db);

        Assert.Equal(4, await db.Tickets.CountAsync(t => t.Source == TicketSource.Portal));
    }

    [Fact]
    public async Task Seed_e_idempotente()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var seeder = new DatabaseSeeder(
            db, new PasswordHasher<User>(), NullLogger<DatabaseSeeder>.Instance);

        await seeder.SeedAsync(includeDevelopmentUsers: true);
        var afterFirst = (
            Categories: await db.Categories.CountAsync(),
            Policies: await db.SlaPolicies.CountAsync(),
            Holidays: await db.Holidays.CountAsync(),
            Users: await db.Users.CountAsync());

        await seeder.SeedAsync(includeDevelopmentUsers: true);
        var afterSecond = (
            Categories: await db.Categories.CountAsync(),
            Policies: await db.SlaPolicies.CountAsync(),
            Holidays: await db.Holidays.CountAsync(),
            Users: await db.Users.CountAsync());

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(12, afterFirst.Categories);
        Assert.Equal(4, afterFirst.Policies);
        Assert.Equal(3, afterFirst.Users);
    }

    [Fact]
    public async Task Seed_nao_cria_usuario_de_exemplo_fora_de_desenvolvimento()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var seeder = new DatabaseSeeder(
            db, new PasswordHasher<User>(), NullLogger<DatabaseSeeder>.Instance);

        await seeder.SeedAsync(includeDevelopmentUsers: false);

        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(12, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Senha_do_usuario_de_exemplo_e_verificavel_pelo_PasswordHasher()
    {
        // Invariante 8: o hash sai do PasswordHasher. Se alguém trocar por MD5, o
        // VerifyHashedPassword deixa de reconhecer o formato e este teste quebra.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var hasher = new PasswordHasher<User>();
        var seeder = new DatabaseSeeder(db, hasher, NullLogger<DatabaseSeeder>.Instance);
        await seeder.SeedAsync(includeDevelopmentUsers: true);

        var user = await db.Users.SingleAsync(u => u.Role == UserRole.Manager);

        var result = hasher.VerifyHashedPassword(
            user, user.PasswordHash!, DatabaseSeeder.DevelopmentPassword);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public async Task Feriados_do_seed_batem_com_o_calendario_brasileiro()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var seeder = new DatabaseSeeder(
            db, new PasswordHasher<User>(), NullLogger<DatabaseSeeder>.Instance);
        await seeder.SeedAsync(includeDevelopmentUsers: false);

        var holidays = await db.Holidays.Select(h => h.Date).ToListAsync();
        var year = DateTime.UtcNow.Year;

        Assert.Contains(new DateOnly(year, 12, 25), holidays);
        Assert.Contains(new DateOnly(year, 9, 7), holidays);
        // Três anos de calendário, treze feriados por ano.
        Assert.Equal(39, holidays.Count);
    }
}
