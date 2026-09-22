using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectTaskServiceTests
{
    [Fact]
    public async Task JsonTaskRunsStructuredArgumentsAndStreamsOutput()
    {
        using var fixture = new TaskFixture();
        fixture.Write(new
        {
            SchemaVersion = 1,
            Tasks = new[] { new { Id = "sdk", Name = "Check SDK", Description = "Confirm the selected .NET SDK.", Executable = "dotnet", Arguments = new[] { "--version" }, WorkingDirectory = ".", TimeoutSeconds = 30 } }
        });
        var service = new ProjectTaskService();
        var plan = Assert.Single(service.Load(fixture.Root).Tasks);
        var lines = new List<string>();
        var result = await service.RunAsync(plan, line => lines.Add(line));

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Contains(lines, line => char.IsDigit(line.FirstOrDefault()));
        Assert.Equal("sdk", plan.Id);
        Assert.Equal("Confirm the selected .NET SDK.", plan.Description);
        Assert.Equal(fixture.Root, plan.WorkingDirectory);
    }

    [Fact]
    public async Task ChangedTaskConfigCannotRunPreviouslyInspectedPlan()
    {
        using var fixture = new TaskFixture();
        fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "sdk", Name = "Check SDK", Executable = "dotnet", Arguments = new[] { "--version" } } } });
        var service = new ProjectTaskService();
        var plan = Assert.Single(service.Load(fixture.Root).Tasks);
        fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "sdk", Name = "Changed", Executable = "dotnet", Arguments = new[] { "--info" } } } });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(plan));
    }

    [Fact]
    public async Task ConfigChangedAtProcessStartCannotRunInspectedPlan()
    {
        using var fixture = new TaskFixture();
        fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "sdk", Name = "Check SDK", Executable = "dotnet", Arguments = new[] { "--version" } } } });
        var runner = new ChangingAtStartRunner(() => fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "sdk", Name = "Changed SDK", Executable = "dotnet", Arguments = new[] { "--info" } } } }));
        var service = new ProjectTaskService(runner);
        var plan = Assert.Single(service.Load(fixture.Root).Tasks);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(plan));
        Assert.False(runner.Started);
    }

    [Fact]
    public void TaskWorkingDirectoryCannotEscapeWorkingCopy()
    {
        using var fixture = new TaskFixture();
        fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "outside", Name = "Outside", Executable = "dotnet", WorkingDirectory = "../", Arguments = new[] { "--version" } } } });
        Assert.Throws<InvalidDataException>(() => new ProjectTaskService().Load(fixture.Root));
    }

    [Fact]
    public void UnknownTaskSettingFailsInsteadOfSilentlyChangingExecution()
    {
        using var fixture = new TaskFixture();
        fixture.Write(new { SchemaVersion = 1, Tasks = new[] { new { Id = "sdk", Name = "Check SDK", Executable = "dotnet", TimeoutSecounds = 30 } } });
        Assert.Throws<InvalidDataException>(() => new ProjectTaskService().Load(fixture.Root));
    }

    private sealed class TaskFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "powerforge-task-" + Guid.NewGuid().ToString("N"));
        public TaskFixture() => Directory.CreateDirectory(Path.Combine(Root, "Build"));
        public void Write<T>(T value) => File.WriteAllText(Path.Combine(Root, "Build", "powerforge.tasks.json"), JsonSerializer.Serialize(value));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class ChangingAtStartRunner(Action changeConfiguration) : IProcessRunner
    {
        public bool Started { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            changeConfiguration();
            request.InvokePreStartBoundary();
            Started = true;
            throw new InvalidOperationException("The process should never start.");
        }
    }
}
