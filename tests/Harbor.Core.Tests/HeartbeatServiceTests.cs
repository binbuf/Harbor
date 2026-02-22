using Harbor.Core.Services;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for HeartbeatService: heartbeat writing, reading, and elapsed time calculation.
/// </summary>
public class HeartbeatServiceTests : IDisposable
{
    private readonly HeartbeatService _service = new();

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Constructor_WritesInitialHeartbeat()
    {
        long heartbeat = _service.ReadLastHeartbeat();

        Assert.NotEqual(0, heartbeat);
    }

    [Fact]
    public void ReadLastHeartbeat_ReturnsRecentTimestamp()
    {
        long heartbeat = _service.ReadLastHeartbeat();
        double elapsedMs = HeartbeatService.GetElapsedMs(heartbeat);

        // Should have been written within the last second
        Assert.True(elapsedMs < 1000, $"Heartbeat is {elapsedMs}ms old, expected < 1000ms");
    }

    [Fact]
    public async Task Heartbeat_UpdatesOverTime()
    {
        long first = _service.ReadLastHeartbeat();

        // Wait for at least one heartbeat interval
        await Task.Delay(600);

        long second = _service.ReadLastHeartbeat();

        // The timestamp should have advanced
        Assert.True(second > first, "Heartbeat timestamp should advance over time");
    }

    [Fact]
    public void GetElapsedMs_RecentTimestamp_ReturnsSmallValue()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsed = HeartbeatService.GetElapsedMs(now);

        Assert.True(elapsed < 100, $"Elapsed was {elapsed}ms, expected < 100ms");
    }

    [Fact]
    public void GetElapsedMs_OldTimestamp_ReturnsLargerValue()
    {
        // Simulate a timestamp from 2 seconds ago
        long freq = System.Diagnostics.Stopwatch.Frequency;
        long twoSecondsAgo = System.Diagnostics.Stopwatch.GetTimestamp() - (2 * freq);

        double elapsed = HeartbeatService.GetElapsedMs(twoSecondsAgo);

        Assert.True(elapsed >= 1900 && elapsed <= 2500,
            $"Elapsed was {elapsed}ms, expected ~2000ms");
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(500, HeartbeatService.IntervalMs);
        Assert.Equal(8, HeartbeatService.MmfSize);
    }
}
