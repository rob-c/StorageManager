using StorageManager.Connection;

namespace StorageManager.Core.Tests.Connection;

public class ConnectionMonitorTests
{
    private sealed class Clock { public DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc); public void Advance(TimeSpan t) => Now += t; }

    private sealed class Harness
    {
        public readonly Clock Clock = new();
        public bool Healthy = true;
        public bool HasTicket = true;
        public bool ReconnectSucceeds = true;
        public int Teardowns;
        public int ReconnectAttempts;

        public ConnectionMonitor Build(MonitorOptions? opts = null) => new(
            checkHealthy: _ => Task.FromResult(Healthy),
            teardown: _ => { Teardowns++; return Task.CompletedTask; },
            reconnect: _ => { ReconnectAttempts++; if (ReconnectSucceeds) Healthy = true; return Task.FromResult(ReconnectSucceeds); },
            hasValidTicket: () => HasTicket,
            clock: () => Clock.Now,
            options: opts);
    }

    [Fact]
    public async Task Healthy_tick_does_nothing()
    {
        var h = new Harness();
        var m = h.Build();
        Assert.Equal(MonitorTickResult.Healthy, await m.TickAsync());
        Assert.Equal(0, h.Teardowns);
    }

    [Fact]
    public async Task Unhealthy_within_grace_only_watches()
    {
        var h = new Harness();
        var m = h.Build();
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10)); // < 15s
        Assert.Equal(MonitorTickResult.Watching, await m.TickAsync());
        Assert.Equal(0, h.Teardowns);
    }

    [Fact]
    public async Task Broken_15s_with_ticket_tears_down_and_reconnects()
    {
        var h = new Harness { ReconnectSucceeds = true };
        var m = h.Build();
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(MonitorTickResult.ReconnectSucceeded, await m.TickAsync());
        Assert.Equal(1, h.Teardowns);
        Assert.Equal(1, h.ReconnectAttempts);
        // Now healthy again.
        Assert.Equal(MonitorTickResult.Healthy, await m.TickAsync());
    }

    [Fact]
    public async Task Broken_15s_without_ticket_needs_manual_reconnect()
    {
        var h = new Harness { HasTicket = false };
        var m = h.Build();
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(MonitorTickResult.NeedsManualReconnect, await m.TickAsync());
        Assert.Equal(1, h.Teardowns);
        Assert.Equal(0, h.ReconnectAttempts); // no ticket → don't even try
        Assert.True(m.Stopped);
    }

    [Fact]
    public async Task Teardown_happens_once_across_failing_ticks_then_gives_up_at_cap()
    {
        var h = new Harness { ReconnectSucceeds = false };
        var m = h.Build(new MonitorOptions(TimeSpan.FromSeconds(15), MaxReconnectAttempts: 3));
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(16));

        // Tick repeatedly; reconnect keeps failing. Advance the clock past the
        // backoff each round so attempts are actually spent (not gated).
        var results = new List<MonitorTickResult>();
        for (var i = 0; i < 8; i++)
        {
            results.Add(await m.TickAsync());
            h.Clock.Advance(TimeSpan.FromSeconds(90));
        }

        Assert.Equal(1, h.Teardowns);                 // only torn down once
        Assert.Equal(3, h.ReconnectAttempts);          // capped at MaxReconnectAttempts
        Assert.Equal(MonitorTickResult.NeedsManualReconnect, results[^1]);
    }

    [Fact]
    public async Task Backoff_gates_attempts_without_advancing_time()
    {
        var h = new Harness { ReconnectSucceeds = false };
        var m = h.Build(new MonitorOptions(TimeSpan.FromSeconds(15), MaxReconnectAttempts: 5));
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(16));

        await m.TickAsync();                 // first attempt: immediate
        Assert.Equal(1, h.ReconnectAttempts);
        await m.TickAsync();                 // no time passed → gated by backoff
        await m.TickAsync();
        Assert.Equal(1, h.ReconnectAttempts); // still 1: backoff not elapsed
        Assert.Equal(MonitorTickResult.Reconnecting, m.LastResult);
    }

    [Fact]
    public async Task Reconnect_that_does_not_restore_health_keeps_counting_attempts()
    {
        // Reconnect claims success but the link stays broken → must not reset the counter.
        var h = new Harness();
        var m = h.Build(new MonitorOptions(TimeSpan.FromSeconds(15), MaxReconnectAttempts: 2));
        h.Healthy = false;
        h.ReconnectSucceeds = true;      // returns true...
        // ...but the reconnect delegate does NOT actually restore health:
        var mm = new ConnectionMonitor(
            checkHealthy: _ => Task.FromResult(false),
            teardown: _ => { h.Teardowns++; return Task.CompletedTask; },
            reconnect: _ => { h.ReconnectAttempts++; return Task.FromResult(true); },
            hasValidTicket: () => true,
            clock: () => h.Clock.Now,
            options: new MonitorOptions(TimeSpan.FromSeconds(15), 2));
        h.Clock.Advance(TimeSpan.FromSeconds(16));

        var r1 = await mm.TickAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(90));
        var r2 = await mm.TickAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(90));
        var r3 = await mm.TickAsync();

        Assert.Equal(MonitorTickResult.NeedsManualReconnect, r3); // hit the cap, not an infinite loop
    }

    [Fact]
    public async Task Recovery_before_threshold_resets_watch()
    {
        var h = new Harness();
        var m = h.Build();
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(MonitorTickResult.Watching, await m.TickAsync());
        h.Healthy = true;
        Assert.Equal(MonitorTickResult.Healthy, await m.TickAsync());
        h.Healthy = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10)); // only 10s since last healthy
        Assert.Equal(MonitorTickResult.Watching, await m.TickAsync());
        Assert.Equal(0, h.Teardowns);
    }
}
