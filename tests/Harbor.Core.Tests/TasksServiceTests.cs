using Harbor.Core.Services;

namespace Harbor.Core.Tests;

[Collection("ShellServices")]
public class TasksServiceTests
{
    private readonly ShellServicesFixture _fixture;

    public TasksServiceTests(ShellServicesFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void TasksService_IsNotNull()
    {
        Assert.NotNull(_fixture.Services.TasksService);
    }

    [Fact]
    public void Tasks_IsNotNull()
    {
        Assert.NotNull(_fixture.Services.Tasks);
    }

    [Fact]
    public void Tasks_GroupedWindows_IsNotNull()
    {
        Assert.NotNull(_fixture.Services.Tasks.GroupedWindows);
    }

    [Fact]
    public void Tasks_GroupedWindows_IsBindable()
    {
        // GroupedWindows returns an ICollectionView suitable for WPF binding
        var grouped = _fixture.Services.Tasks.GroupedWindows;
        Assert.IsAssignableFrom<System.ComponentModel.ICollectionView>(grouped);
    }
}
