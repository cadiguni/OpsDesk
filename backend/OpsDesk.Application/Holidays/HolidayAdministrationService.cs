using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Holidays;

public record HolidayItem(Guid Id, DateOnly Date, string Name, bool IsActive, bool IsCustom);

public record AddHolidayRequest(DateOnly Date, string Name);

public record ChangeHolidayActivationRequest(bool IsActive);

public class AddHolidayRequestValidator : AbstractValidator<AddHolidayRequest>
{
    public AddHolidayRequestValidator()
    {
        RuleFor(r => r.Date).NotEmpty().WithMessage("Informe a data.");

        RuleFor(r => r.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Informe o nome do feriado.")
            .MaximumLength(200).WithMessage("O nome pode ter no máximo 200 caracteres.");
    }
}

public abstract record HolidayChangeResult
{
    public sealed record Changed(HolidayItem Holiday) : HolidayChangeResult;

    public sealed record NotFound : HolidayChangeResult;

    public sealed record DateTaken : HolidayChangeResult
    {
        public string Message => "Já existe um feriado nessa data. Se ele está desligado, religue-o.";
    }

    public sealed record NationalCannotBeDeleted : HolidayChangeResult
    {
        public string Message =>
            "Feriado nacional não é apagado, é desligado: a renovação anual o recriaria no dia seguinte.";
    }
}

/// <summary>
/// Feriados do expediente, pelo gestor (README, seção 13.11).
///
/// Os nacionais vêm do sistema e se renovam sozinhos; o gestor pode desligar um deles — a
/// empresa trabalha naquele dia — e cadastrar os que nenhuma tabela nacional tem:
/// municipais, estaduais, recesso de fim de ano.
///
/// Mudar feriado não recalcula prazo de chamado já aberto. O prazo é gravado na abertura
/// (README, seção 8), e reescrevê-lo agora mudaria em silêncio o compromisso assumido com
/// quem abriu.
/// </summary>
public class HolidayAdministrationService(
    IOpsDeskDbContext db, IHolidayCache cache, ILogger<HolidayAdministrationService> logger)
{
    public Task<List<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);

        return db.Holidays
            .AsNoTracking()
            .Where(h => h.Date >= first && h.Date <= last)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayItem(h.Id, h.Date, h.Name, h.IsActive, h.IsCustom))
            .ToListAsync(cancellationToken);
    }

    public async Task<HolidayChangeResult> AddAsync(
        AddHolidayRequest request, CancellationToken cancellationToken = default)
    {
        if (await db.Holidays.AnyAsync(h => h.Date == request.Date, cancellationToken))
        {
            return new HolidayChangeResult.DateTaken();
        }

        var holiday = new Holiday { Date = request.Date, Name = request.Name.Trim(), IsCustom = true };
        db.Holidays.Add(holiday);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new HolidayChangeResult.DateTaken();
        }

        cache.Invalidate();
        logger.LogInformation("Feriado {Date} ({Name}) cadastrado.", holiday.Date, holiday.Name);

        return new HolidayChangeResult.Changed(ToItem(holiday));
    }

    public async Task<HolidayChangeResult> ChangeActivationAsync(
        Guid id, ChangeHolidayActivationRequest request, CancellationToken cancellationToken = default)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (holiday is null)
        {
            return new HolidayChangeResult.NotFound();
        }

        if (holiday.IsActive != request.IsActive)
        {
            holiday.IsActive = request.IsActive;
            await db.SaveChangesAsync(cancellationToken);

            cache.Invalidate();
            logger.LogInformation(
                "Feriado {Date} {Action}.", holiday.Date, request.IsActive ? "religado" : "desligado");
        }

        return new HolidayChangeResult.Changed(ToItem(holiday));
    }

    public async Task<HolidayChangeResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (holiday is null)
        {
            return new HolidayChangeResult.NotFound();
        }

        if (!holiday.IsCustom)
        {
            return new HolidayChangeResult.NationalCannotBeDeleted();
        }

        db.Holidays.Remove(holiday);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate();
        logger.LogInformation("Feriado {Date} ({Name}) apagado.", holiday.Date, holiday.Name);

        return new HolidayChangeResult.Changed(ToItem(holiday));
    }

    private static HolidayItem ToItem(Holiday h) => new(h.Id, h.Date, h.Name, h.IsActive, h.IsCustom);
}
