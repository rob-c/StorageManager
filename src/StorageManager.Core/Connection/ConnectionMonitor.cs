namespace StorageManager.Connection;

/// <summary>The outcome of a single monitor poll.</summary>
public enum MonitorTickResult
{
    /// <summary>Master + mount are both live.</summary>
    Healthy,
    /// <summary>Unhealthy, but still within the grace window before teardown.</summary>
    Watching,
    /// <summary>Was torn down and successfully re-established.</summary>
    ReconnectSucceeded,
    /// <summary>Torn down; a reconnect attempt is in progress / will retry.</summary>
    Reconnecting,
    /// <summary>Torn down and giving up — the user must reconnect manually.</summary>
    NeedsManualReconnect,
}

/// <summary>Tunable thresholds for the watchdog.</summary>
public sealed record MonitorOptions(TimeSpan BreakThreshold, int MaxReconnectAttempts)
{
    public static MonitorOptions Default { get; } = new(TimeSpan.FromSeconds(15), 5);
}

/// <summary>
/// Watchdog state machine for a jump-host connection. Poll it on a timer with
/// <see cref="TickAsync"/>. When the connection is unhealthy continuously for
/// <see cref="MonitorOptions.BreakThreshold"/> (15s), it tears the connection
/// down (unmount + drop the master socket) and then auto-reconnects while a
/// valid Kerberos ticket exists, up to a bounded number of attempts; after that
/// it stops and asks for a manual reconnect. All side effects are injected, so
/// the logic is testable with a fake clock and fake operations.
/// </summary>
public sealed class ConnectionMonitor
{
    private readonly Func<CancellationToken, Task<bool>> _checkHealthy;
    private readonly Func<CancellationToken, Task> _teardown;
    private readonly Func<CancellationToken, Task<bool>> _reconnect;
    private readonly Func<bool> _hasValidTicket;
    private readonly Func<DateTime> _clock;
    private readonly MonitorOptions _opts;

    // Backoff between reconnect attempts (first is immediate), so MaxReconnectAttempts
    // spans a couple of minutes rather than being burned in a few ticks.
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15),
         TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private DateTime _lastHealthy;
    private DateTime? _lastAttempt;
    private int _reconnectAttempts;
    private bool _tornDown;

    public ConnectionMonitor(
        Func<CancellationToken, Task<bool>> checkHealthy,
        Func<CancellationToken, Task> teardown,
        Func<CancellationToken, Task<bool>> reconnect,
        Func<bool> hasValidTicket,
        Func<DateTime> clock,
        MonitorOptions? options = null)
    {
        _checkHealthy = checkHealthy;
        _teardown = teardown;
        _reconnect = reconnect;
        _hasValidTicket = hasValidTicket;
        _clock = clock;
        _opts = options ?? MonitorOptions.Default;
        _lastHealthy = clock();
    }

    public MonitorTickResult LastResult { get; private set; } = MonitorTickResult.Healthy;
    public bool Stopped => LastResult == MonitorTickResult.NeedsManualReconnect;

    /// <summary>Marks the connection freshly healthy (call after a manual reconnect).</summary>
    public void MarkHealthy()
    {
        _lastHealthy = _clock();
        _lastAttempt = null;
        _reconnectAttempts = 0;
        _tornDown = false;
        LastResult = MonitorTickResult.Healthy;
    }

    public async Task<MonitorTickResult> TickAsync(CancellationToken ct = default)
    {
        // Once we've given up, stay stopped until an external reconnect resets us.
        if (Stopped)
            return MonitorTickResult.NeedsManualReconnect;

        if (await _checkHealthy(ct))
        {
            MarkHealthy();
            return LastResult;
        }

        // Unhealthy but still inside the grace window, and not yet torn down.
        if (!_tornDown && _clock() - _lastHealthy < _opts.BreakThreshold)
            return LastResult = MonitorTickResult.Watching;

        // Threshold crossed (or already torn down): ensure teardown happened once.
        if (!_tornDown)
        {
            await _teardown(ct);
            _tornDown = true;
            _lastAttempt = null;
        }

        if (!_hasValidTicket() || _reconnectAttempts >= _opts.MaxReconnectAttempts)
            return LastResult = MonitorTickResult.NeedsManualReconnect;

        // Wait out the backoff before spending another attempt.
        var wait = Backoff[Math.Min(_reconnectAttempts, Backoff.Length - 1)];
        if (_lastAttempt is { } last && _clock() - last < wait)
            return LastResult = MonitorTickResult.Reconnecting;

        _reconnectAttempts++;
        _lastAttempt = _clock();
        // Trust a reconnect only if a real health check agrees — otherwise a
        // reconnect that "succeeds" but leaves the link broken would reset the
        // attempt counter forever and the retry cap could never be reached.
        if (await _reconnect(ct) && await _checkHealthy(ct))
        {
            MarkHealthy();
            return LastResult = MonitorTickResult.ReconnectSucceeded;
        }

        return LastResult = MonitorTickResult.Reconnecting;
    }
}
