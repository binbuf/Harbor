using Harbor.Core.Services;
using ManagedShell;

namespace Harbor.Core.Tests;

/// <summary>
/// Shared fixture for ShellServices tests. ManagedShell's TasksService uses WPF
/// DependencyProperty which can only be registered once per process, so we share
/// a single ShellManager instance across all tests in this collection.
/// </summary>
public class ShellServicesFixture : IDisposable
{
    public ShellServices Services { get; }

    public ShellServicesFixture()
    {
        Services = new ShellServices();
    }

    public void Dispose()
    {
        Services.Dispose();
    }
}

[CollectionDefinition("ShellServices")]
public class ShellServicesCollection : ICollectionFixture<ShellServicesFixture>;

[Collection("ShellServices")]
public class ShellServicesTests
{
    private readonly ShellServicesFixture _fixture;

    public ShellServicesTests(ShellServicesFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_InitializesAllServices()
    {
        var services = _fixture.Services;

        Assert.NotNull(services.ShellManager);
        Assert.NotNull(services.TasksService);
        Assert.NotNull(services.Tasks);
        Assert.NotNull(services.NotificationArea);
        Assert.NotNull(services.AppBarManager);
        Assert.NotNull(services.FullScreenHelper);
        Assert.NotNull(services.ExplorerHelper);
    }

    [Fact]
    public void Tasks_GroupedWindows_IsAccessible()
    {
        var grouped = _fixture.Services.Tasks.GroupedWindows;
        Assert.NotNull(grouped);
    }

    [Fact]
    public void NotificationArea_TrayIcons_IsAccessible()
    {
        Assert.NotNull(_fixture.Services.NotificationArea.TrayIcons);
    }

    [Fact]
    public void NotificationArea_IsFailed_IsFalse()
    {
        Assert.False(_fixture.Services.NotificationArea.IsFailed);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Test idempotent disposal using the shared fixture's underlying ShellManager.
        // We verify that calling Dispose twice doesn't throw by creating a separate
        // ShellServices wrapper around the same default config. Since DependencyProperty
        // is already registered from the fixture, we test the disposal path directly.
        var services = _fixture.Services;

        // Calling Dispose on the fixture's services won't actually dispose yet
        // (fixture manages lifetime), but we can verify the method is safe to call.
        // The fixture's Dispose() will handle actual cleanup.
        // Instead, verify our Dispose guard flag works:
        services.Dispose();
        services.Dispose(); // Should not throw - idempotent
    }
}
