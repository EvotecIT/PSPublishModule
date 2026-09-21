using System.Net;
using System.Text;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Automation;

namespace PowerForgeStudio.Tests;

public sealed class GitHubWorkflowRuntimeSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "studio-workflow-runtime-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task MatchesWorkflowPathAndScheduledRunWithoutInventingNextOccurrence()
    {
        var rows = await DefinitionsAsync("maintenance.yml", "disabled.yml");
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/workflows")
            ? Json(HttpStatusCode.OK, """
                {"total_count":2,"workflows":[
                  {"id":7,"path":".github/workflows/maintenance.yml","state":"active"},
                  {"id":8,"path":".github/workflows/disabled.yml","state":"disabled_manually"}]}
                """)
            : Json(HttpStatusCode.OK, """
                {"total_count":1,"workflow_runs":[
                  {"workflow_id":7,"status":"completed","conclusion":"failure","created_at":"2026-09-21T02:00:00Z","run_number":42}]}
                """));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var source = new GitHubWorkflowRuntimeSource(client, (_, _) => Task.FromResult<string?>("EvotecIT/Fixture"));

        var result = await source.EnrichAsync(rows, CancellationToken.None);

        Assert.Equal("Runtime observed", result.State.State);
        var maintenance = Assert.Single(result.Entries, item => item.Name == "maintenance");
        Assert.Equal("Failed", maintenance.State);
        Assert.True(maintenance.HasRuntimeEvidence);
        Assert.True(maintenance.IsEnabled);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T02:00:00Z"), maintenance.LastRunAt);
        Assert.Contains("run #42", maintenance.LastResult);
        Assert.Null(maintenance.NextRunAt);
        var disabled = Assert.Single(result.Entries, item => item.Name == "disabled");
        Assert.Equal("Disabled", disabled.State);
        Assert.True(disabled.HasRuntimeEvidence);
        Assert.False(disabled.IsEnabled);
        Assert.Equal(2, handler.Paths.Count);
        Assert.Contains("event=schedule", handler.Paths[1]);
    }

    [Fact]
    public async Task FailedRemoteReadPreservesLocalDefinitionWithPartialProviderEvidence()
    {
        var rows = await DefinitionsAsync("maintenance.yml");
        var handler = new Handler(_ => Json(HttpStatusCode.Forbidden, "{}"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var source = new GitHubWorkflowRuntimeSource(client, (_, _) => Task.FromResult<string?>("EvotecIT/Fixture"));

        var result = await source.EnrichAsync(rows, CancellationToken.None);

        Assert.Equal("Partial", result.State.State);
        var item = Assert.Single(result.Entries);
        Assert.Equal("Definition only", item.State);
        Assert.False(item.HasRuntimeEvidence);
        Assert.Null(item.NextRunAt);
    }

    [Fact]
    public async Task CappedRemoteListingsDoNotClaimCompleteScheduleCoverage()
    {
        var rows = await DefinitionsAsync("maintenance.yml");
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/workflows")
            ? Json(HttpStatusCode.OK, """{"total_count":101,"workflows":[{"id":7,"path":".github/workflows/maintenance.yml","state":"active"}]}""")
            : Json(HttpStatusCode.OK, """{"total_count":101,"workflow_runs":[]}"""));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var source = new GitHubWorkflowRuntimeSource(client, (_, _) => Task.FromResult<string?>("EvotecIT/Fixture"));

        var result = await source.EnrichAsync(rows, CancellationToken.None);

        Assert.Equal("Partial", result.State.State);
        var item = Assert.Single(result.Entries);
        Assert.Equal("Enabled", item.State);
        Assert.Equal("No scheduled run in latest 100 repository runs", item.LastResult);
        Assert.Null(item.NextRunAt);
    }

    [Fact]
    public async Task UsesNewestRunPerWorkflowEvenWhenResponseOrderDiffers()
    {
        var rows = await DefinitionsAsync("maintenance.yml");
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/workflows")
            ? Json(HttpStatusCode.OK, """{"total_count":1,"workflows":[{"id":7,"path":".github/workflows/maintenance.yml","state":"active"}]}""")
            : Json(HttpStatusCode.OK, """
                {"total_count":2,"workflow_runs":[
                  {"workflow_id":7,"status":"completed","conclusion":"failure","created_at":"2026-09-20T02:00:00Z","run_number":41},
                  {"workflow_id":7,"status":"completed","conclusion":"success","created_at":"2026-09-21T02:00:00Z","run_number":42}]}
                """));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var source = new GitHubWorkflowRuntimeSource(client, (_, _) => Task.FromResult<string?>("EvotecIT/Fixture"));

        var result = await source.EnrichAsync(rows, CancellationToken.None);

        var item = Assert.Single(result.Entries);
        Assert.Equal("Enabled", item.State);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T02:00:00Z"), item.LastRunAt);
        Assert.Contains("success", item.LastResult);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsPartialProviderEvidence()
    {
        var rows = await DefinitionsAsync("maintenance.yml");
        using var client = new HttpClient(new Handler(_ => Json(HttpStatusCode.OK, "{}")))
            { BaseAddress = new Uri("https://api.github.com") };
        using var cancellation = new CancellationTokenSource();
        using var source = new GitHubWorkflowRuntimeSource(client, (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<string?>(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.EnrichAsync(rows, cancellation.Token));
    }

    [Theory]
    [InlineData("timed_out")]
    [InlineData("action_required")]
    [InlineData("startup_failure")]
    public async Task ActionableRunOutcomesAppearInAttentionFilter(string conclusion)
    {
        var rows = await DefinitionsAsync("maintenance.yml");
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/workflows")
            ? Json(HttpStatusCode.OK, """{"total_count":1,"workflows":[{"id":7,"path":".github/workflows/maintenance.yml","state":"active"}]}""")
            : Json(HttpStatusCode.OK, """{"total_count":1,"workflow_runs":[{"workflow_id":7,"status":"completed","conclusion":"OUTCOME","created_at":"2026-09-21T02:00:00Z","run_number":42}]}""".Replace("OUTCOME", conclusion)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var source = new GitHubWorkflowRuntimeSource(client, (_, _) => Task.FromResult<string?>("EvotecIT/Fixture"));

        var result = await source.EnrichAsync(rows, CancellationToken.None);

        var item = Assert.Single(result.Entries);
        Assert.Equal("Failed", item.State);
        Assert.True(item.NeedsAttention);
        Assert.Contains(conclusion, item.LastResult);
    }

    private async Task<AutomationSourceResult> DefinitionsAsync(params string[] names)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, ".github", "workflows"));
        var rows = new List<WorkspaceAutomationEntry>();
        foreach (var name in names)
        {
            var file = Path.Combine(folder.FullName, name);
            await File.WriteAllTextAsync(file, "on:\n  schedule:\n    - cron: '5 4 * * *'\n");
            rows.Add(new($"{_root}|.github/workflows/{name}|3", Path.GetFileNameWithoutExtension(name),
                "GitHub Actions", $".github/workflows/{name}", "Fixture", "5 4 * * *", "Definition only",
                null, null, "Runtime not checked", false, true, true, file, "Local definition only."));
        }
        return new(rows, new("GitHub Actions", "Definitions", rows.Count, "Local definitions only."));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(response(request));
        }
    }
}
