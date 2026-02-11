namespace EvidenceFoundry.Helpers;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset LocalNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}

public static class Clock
{
    private static IClock _current = new SystemClock();

    public static IClock Current
    {
        get => _current;
        set => _current = value ?? new SystemClock();
    }

    public static DateTimeOffset UtcNow => Current.UtcNow;
    public static DateTimeOffset LocalNow => Current.LocalNow;

    public static DateTime UtcNowDateTime => UtcNow.UtcDateTime;
    public static DateTime LocalNowDateTime => LocalNow.LocalDateTime;

    public static DateTime EnsureKind(DateTime value, DateTimeKind defaultKind)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, defaultKind)
            : value;
    }

    public static DateTimeOffset EnsureOffset(DateTime value, DateTimeKind defaultKind)
    {
        var normalized = EnsureKind(value, defaultKind);
        return new DateTimeOffset(normalized);
    }
}
