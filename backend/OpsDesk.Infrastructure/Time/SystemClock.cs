using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Time;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
