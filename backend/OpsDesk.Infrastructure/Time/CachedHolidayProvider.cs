using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpsDesk.Application.Abstractions;
using OpsDesk.Infrastructure.Persistence;

namespace OpsDesk.Infrastructure.Time;

/// <summary>
/// Feriados lidos do banco e mantidos em memória. A tabela é minúscula e muda uma vez por
/// ano, mas é consultada em todo cálculo de prazo — sem cache, abrir um chamado custaria
/// uma query a mais por conta de dado praticamente estático.
///
/// A leitura é síncrona de propósito: <see cref="IBusinessCalendar"/> é síncrono para que
/// a conta de horas úteis não contamine com <c>async</c> todo serviço que calcula prazo.
/// </summary>
public class CachedHolidayProvider(OpsDeskDbContext db, IMemoryCache cache) : IHolidayProvider
{
    private const string CacheKey = "opsdesk:holidays";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public IReadOnlySet<DateOnly> GetHolidays() =>
        cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            return db.Holidays
                .AsNoTracking()
                .Select(h => h.Date)
                .ToHashSet();
        })!;
}
