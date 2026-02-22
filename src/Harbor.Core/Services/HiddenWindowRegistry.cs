using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Harbor.Core.Services;

/// <summary>
/// Maintains a memory-mapped file recording every HWND hidden via SW_HIDE.
/// Readable by both the main process and the watchdog.
/// Format: [4-byte count][N × 8-byte HWND values (as Int64)]
/// </summary>
public sealed class HiddenWindowRegistry : IDisposable
{
    public const string MmfName = "Harbor_HiddenWindowRegistry";
    public const int MaxEntries = 512;
    public const int HeaderSize = 4; // 4-byte count
    public const int EntrySize = 8;  // 8-byte Int64 for HWND
    public const int TotalSize = HeaderSize + (MaxEntries * EntrySize);

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly object _lock = new();
    private bool _disposed;

    public HiddenWindowRegistry()
    {
        _mmf = MemoryMappedFile.CreateOrOpen(MmfName, TotalSize);
        _accessor = _mmf.CreateViewAccessor(0, TotalSize);

        Trace.WriteLine("[Harbor] HiddenWindowRegistry: Initialized.");
    }

    /// <summary>
    /// Records a hidden window handle. Thread-safe and atomic.
    /// </summary>
    public void Add(nint hwnd)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int count = _accessor.ReadInt32(0);
            if (count >= MaxEntries)
            {
                Trace.WriteLine($"[Harbor] HiddenWindowRegistry: Max entries ({MaxEntries}) reached, cannot add {hwnd}.");
                return;
            }

            // Check for duplicates
            for (int i = 0; i < count; i++)
            {
                long offset = HeaderSize + (i * EntrySize);
                long existing = _accessor.ReadInt64(offset);
                if (existing == hwnd) return;
            }

            long writeOffset = HeaderSize + (count * EntrySize);
            _accessor.Write(writeOffset, (long)hwnd);
            _accessor.Write(0, count + 1);
            _accessor.Flush();

            Trace.WriteLine($"[Harbor] HiddenWindowRegistry: Added HWND {hwnd} (count={count + 1}).");
        }
    }

    /// <summary>
    /// Removes a window handle when it is re-shown. Thread-safe and atomic.
    /// </summary>
    public void Remove(nint hwnd)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int count = _accessor.ReadInt32(0);

            for (int i = 0; i < count; i++)
            {
                long offset = HeaderSize + (i * EntrySize);
                long existing = _accessor.ReadInt64(offset);

                if (existing == hwnd)
                {
                    // Move last entry into this slot (swap-remove)
                    if (i < count - 1)
                    {
                        long lastOffset = HeaderSize + ((count - 1) * EntrySize);
                        long last = _accessor.ReadInt64(lastOffset);
                        _accessor.Write(offset, last);
                    }

                    _accessor.Write(0, count - 1);
                    _accessor.Flush();

                    Trace.WriteLine($"[Harbor] HiddenWindowRegistry: Removed HWND {hwnd} (count={count - 1}).");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Returns all currently registered hidden window handles.
    /// </summary>
    public nint[] GetAll()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int count = _accessor.ReadInt32(0);
            var result = new nint[count];

            for (int i = 0; i < count; i++)
            {
                long offset = HeaderSize + (i * EntrySize);
                result[i] = (nint)_accessor.ReadInt64(offset);
            }

            return result;
        }
    }

    /// <summary>
    /// Returns the number of registered hidden windows.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _accessor.ReadInt32(0);
            }
        }
    }

    /// <summary>
    /// Clears all entries. Used on safe startup to remove stale entries from a previous session.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _accessor.Write(0, 0);
            _accessor.Flush();

            Trace.WriteLine("[Harbor] HiddenWindowRegistry: Cleared all entries.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _accessor.Dispose();
        _mmf.Dispose();

        Trace.WriteLine("[Harbor] HiddenWindowRegistry: Disposed.");
    }
}
