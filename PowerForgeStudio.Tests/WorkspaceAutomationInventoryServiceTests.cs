using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceAutomationInventoryServiceTests : IDisposable
{
    private readonly string _fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-automations-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task InventoryKeepsRuntimeAndDefinitionOnlyEvidenceSeparate()
    {
        var workflowRoot = Directory.CreateDirectory(Path.Combine(_fixture, ".github", "workflows")).FullName;
        await File.WriteAllTextAsync(Path.Combine(workflowRoot, "maintenance.yml"), """
            name: Maintenance
            on:
              schedule:
                - cron: '17 5 * * 1'
            jobs:
              build:
                env:
                  cron: not-a-schedule
            """);
        var runner = new InventoryRunner("""
            [{"Id":"\\EvotecIT Codex Profile Sync","Name":"EvotecIT Codex Profile Sync","TaskPath":"\\","State":"Ready","Description":"Sync reviewed profile assets","LastRun":"2026-09-20T21:50:16+02:00","NextRun":"2026-09-21T00:44:57+02:00","LastResult":0,"Execute":"powershell.exe","WorkingDirectory":"","TriggerType":"MSFT_TaskTimeTrigger","StartBoundary":"2026-09-20T00:44:57+02:00","RepetitionInterval":"PT30M"}]
            """);
        var service = new WorkspaceAutomationInventoryService(runner, new RepositorySource(_fixture));

        var snapshot = await service.InspectAsync(_fixture);

        Assert.Equal(2, snapshot.Entries.Count);
        var windows = Assert.Single(snapshot.Entries, entry => entry.Provider == "Windows Task Scheduler");
        Assert.True(windows.HasRuntimeEvidence);
        Assert.True(windows.IsRelevant);
        Assert.Equal("Upcoming", windows.State);
        Assert.Equal("Succeeded", windows.LastResult);
        var workflow = Assert.Single(snapshot.Entries, entry => entry.Provider == "GitHub Actions");
        Assert.False(workflow.HasRuntimeEvidence);
        Assert.Equal("Definition only", workflow.State);
        Assert.Equal("17 5 * * 1", workflow.Schedule);
        Assert.Equal(3, snapshot.Sources.Count);
        Assert.Contains(snapshot.Sources, source => source.Provider == "Codex" && source.State == "Unavailable");
        Assert.Equal(2_000_000, runner.Request!.MaxCapturedOutputCharacters);
        var probe = Encoding.Unicode.GetString(Convert.FromBase64String(runner.Request.Arguments[^1]));
        Assert.DoesNotContain(".Arguments", probe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowParserRequiresOnScheduleAndKeepsQuotedHash()
    {
        var definitions = GitHubWorkflowAutomationSource.ReadCronDefinitions([
            "env:",
            "  cron: 'wrong'",
            "\"on\":",
            "  schedule:",
            "    - cron: \"5 4 * * 1-5\"",
            "    - cron: '9 8 * * * # literal'",
            "    - cron: \"7 6 * * *\" # daily",
            "jobs:",
            "  test: {}"
        ]);

        Assert.Equal(3, definitions.Count);
        Assert.Equal("5 4 * * 1-5", definitions[0].Expression);
        Assert.Equal("9 8 * * * # literal", definitions[1].Expression);
        Assert.Equal("7 6 * * *", definitions[2].Expression);
    }

    [Fact]
    public async Task ProviderFailureDoesNotHideOtherProviderEvidence()
    {
        var runner = new InventoryRunner("""
            [{"Id":"\\PowerForge","Name":"PowerForge maintenance","TaskPath":"\\","State":"Ready","LastRun":"2026-09-20T21:50:16+02:00","NextRun":"2026-09-21T00:44:57+02:00","LastResult":0,"TriggerType":"MSFT_TaskTimeTrigger"}]
            """);
        var service = new WorkspaceAutomationInventoryService(runner, new FailingRepositorySource());

        var snapshot = await service.InspectAsync(_fixture);

        Assert.Single(snapshot.Entries);
        Assert.Contains(snapshot.Sources, source => source.Provider == "Windows Task Scheduler" && source.State == "Available");
        Assert.Contains(snapshot.Sources, source => source.Provider == "GitHub Actions" && source.State == "Unavailable");
    }

    public void Dispose() => Directory.Delete(_fixture, recursive: true);

    private sealed class RepositorySource(string root) : IWorkspaceRepositorySource
    {
        public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RepositoryCatalogEntry>>([
                new("Fixture", root, ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository,
                    null, null, false, false)
            ]);
    }

    private sealed class FailingRepositorySource : IWorkspaceRepositorySource
    {
        public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => throw new IOException("repository discovery unavailable");
    }

    private sealed class InventoryRunner(string output) : IProcessRunner
    {
        public ProcessRunRequest? Request { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            request.InvokePreStartBoundary();
            request.InvokeStartedProcessBoundary(1);
            request.InvokeStartBoundary();
            var result = new ProcessRunResult(0, output, "", request.FileName, TimeSpan.Zero, false);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }
}
