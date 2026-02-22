using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Harbor.Core.Services;

/// <summary>
/// Writes a heartbeat timestamp to a shared memory-mapped file every 500ms.
/// The watchdog process monitors this to detect crashes.
/// Format: [8-byte Int64 tick count from Stopwatch.GetTimestamp()]
/// </summary>
public sealed class HeartbeatService : IDisposable
{
    public const string MmfName = "Harbor_Heartbeat";
    public const int MmfSize = 8; // Single Int64
    public const int IntervalMs = 500;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly Timer _timer;
    private bool _disposed;

    public HeartbeatService()
    {
        _mmf = MemoryMappedFile.CreateOrOpen(MmfName, MmfSize);
        _accessor = _mmf.CreateViewAccessor(0, MmfSize);

        // Write initial heartbeat immediately
        WriteHeartbeat();

        _timer = new Timer(_ => WriteHeartbeat(), null, IntervalMs, IntervalMs);

        Trace.WriteLine("[Harbor] HeartbeatService: Initialized, writing heartbeat every 500ms.");
    }

    private void WriteHeartbeat()
    {
        if (_disposed) return;

        try
        {
            _accessor.Write(0, Stopwatch.GetTimestamp());
            _accessor.Flush();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
    }

    /// <summary>
    /// Reads the last heartbeat timestamp. Used for testing and by the watchdog.
    /// </summary>
    public long ReadLastHeartbeat()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _accessor.ReadInt64(0);
    }

    /// <summary>
    /// Converts a Stopwatch timestamp to elapsed milliseconds from now.
    /// </summary>
    public static double GetElapsedMs(long timestamp)
    {
        long now = Stopwatch.GetTimestamp();
        long elapsed = now - timestamp;
        return (double)elapsed / Stopwatch.Frequency * 1000.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        _accessor.Dispose();
        _mmf.Dispose();

        Trace.WriteLine("[Harbor] HeartbeatService: Disposed.");
    }
}
