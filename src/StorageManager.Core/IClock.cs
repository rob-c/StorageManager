namespace StorageManager;

/// <summary>Abstracts the wall clock so time-dependent output (e.g. backup names) is testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>The real system clock.</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();
    public DateTime UtcNow => DateTime.UtcNow;
}
