using Harbor.Core.Services;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for CrashRecoveryService: recovery logic, animation restoration,
/// crash dump writing, and stale registry cleanup.
/// </summary>
public class CrashRecoveryServiceTests
{
    [Fact]
    public void ReShowHiddenWindows_EmptyRegistry_ReturnsZero()
    {
        // Ensure a registry exists but is empty
        using var registry = new HiddenWindowRegistry();
        registry.Clear();

        int restored = CrashRecoveryService.ReShowHiddenWindows();

        Assert.Equal(0, restored);
    }

    [Fact]
    public void ReShowHiddenWindows_WithStaleHwnds_SkipsInvalid()
    {
        using var registry = new HiddenWindowRegistry();
        registry.Clear();

        // Add obviously invalid HWNDs
        registry.Add(0x7FFFFFFF);
        registry.Add(0x7FFFFFFE);

        int restored = CrashRecoveryService.ReShowHiddenWindows();

        // Invalid HWNDs should be skipped (IsWindow returns false)
        Assert.Equal(0, restored);
    }

    [Fact]
    public void ReShowHiddenWindows_ClearsRegistryAfterRecovery()
    {
        using var registry = new HiddenWindowRegistry();
        registry.Clear();
        registry.Add(0x7FFFFFFF);

        CrashRecoveryService.ReShowHiddenWindows();

        // Registry should be empty after recovery
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RestoreNativeAnimations_DoesNotThrow()
    {
        // Just verify it doesn't throw — actual SystemParametersInfo
        // behavior is OS-dependent
        var ex = Record.Exception(() => CrashRecoveryService.RestoreNativeAnimations());
        Assert.Null(ex);
    }

    [Fact]
    public void WriteCrashDump_CreatesFile()
    {
        var testException = new InvalidOperationException("Test crash for unit test");

        CrashRecoveryService.WriteCrashDump(testException);

        var dumpDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Harbor", "CrashDumps");

        Assert.True(Directory.Exists(dumpDir));

        // Find the most recently created dump file
        var files = Directory.GetFiles(dumpDir, "crash_*.txt")
            .OrderByDescending(File.GetCreationTimeUtc)
            .ToArray();

        Assert.NotEmpty(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("Test crash for unit test", content);
        Assert.Contains("Harbor Crash Report", content);

        // Cleanup test file
        File.Delete(files[0]);
    }

    [Fact]
    public void WriteCrashDump_NullException_WritesPlaceholder()
    {
        CrashRecoveryService.WriteCrashDump(null);

        var dumpDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Harbor", "CrashDumps");

        var files = Directory.GetFiles(dumpDir, "crash_*.txt")
            .OrderByDescending(File.GetCreationTimeUtc)
            .ToArray();

        Assert.NotEmpty(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("No exception information available", content);

        // Cleanup
        File.Delete(files[0]);
    }

    [Fact]
    public void ClearStaleRegistry_EmptyRegistry_DoesNotThrow()
    {
        using var registry = new HiddenWindowRegistry();
        registry.Clear();

        var ex = Record.Exception(() => CrashRecoveryService.ClearStaleRegistry());
        Assert.Null(ex);
    }

    [Fact]
    public void ClearStaleRegistry_WithEntries_ClearsAndRestoresValid()
    {
        using var registry = new HiddenWindowRegistry();
        registry.Clear();
        registry.Add(0x7FFFFFFF); // Invalid HWND

        CrashRecoveryService.ClearStaleRegistry();

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void ExecuteRecovery_DoesNotThrow()
    {
        // Ensure registry exists
        using var registry = new HiddenWindowRegistry();
        registry.Clear();

        var ex = Record.Exception(() =>
            CrashRecoveryService.ExecuteRecovery(new Exception("test")));
        Assert.Null(ex);
    }
}
