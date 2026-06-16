namespace PowerForge.Tests;

public sealed class GitHubRunnerHousekeepingWorkflowTests
{
    [Fact]
    public void ReusableWorkflow_ShouldUseSharedCompositeAction()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-github-runner-housekeeping.yml");

        Assert.True(File.Exists(workflowPath), $"Runner housekeeping workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);
        var runsOnLine = File.ReadLines(workflowPath)
            .Single(line => line.TrimStart().StartsWith("runs-on:", StringComparison.Ordinal));

        Assert.Contains("PowerForge GitHub Runner Housekeeping", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner-labels", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("fromJson(", runsOnLine, StringComparison.Ordinal);
        Assert.Contains("inputs.runner_labels_json != '' && inputs.runner_labels_json", runsOnLine, StringComparison.Ordinal);
        Assert.Contains("inputs['runner-labels'] != '' && inputs['runner-labels']", runsOnLine, StringComparison.Ordinal);
        Assert.Contains("'[\"self-hosted\",\"linux\"]'", runsOnLine, StringComparison.Ordinal);
        Assert.Contains("./.powerforge/pspublishmodule/.github/actions/github-housekeeping", workflowYaml, StringComparison.Ordinal);
        Assert.Contains(".powerforge/runner-housekeeping.json", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner-min-free-gb", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("report-artifact-name", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CallerWorkflow_ShouldFanOutAcrossNamedLinuxRunners()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "runner-housekeeping.yml");

        Assert.True(File.Exists(workflowPath), $"Runner housekeeping caller workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("cron: \"17 2 * * *\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("github.repository == 'EvotecIT/PSPublishModule' || github.event.repository.private", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("apply: ${{ github.event_name != 'workflow_dispatch' || inputs.apply == 'true' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("fail-fast: false", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux-01", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux-02", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux-03", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux-04", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("name: github-runner-linux-05", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux-01\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux-02\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux-03\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux-04\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("\"runner-github-runner-linux-05\"", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner_labels_json: ${{ matrix.runner.labels }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner-housekeeping-${{ matrix.runner.name }}", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CallerConfig_ShouldAggressivelyCleanCentralRunnerWorkspaces()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, ".powerforge", "runner-housekeeping.json");

        Assert.True(File.Exists(configPath), $"Runner housekeeping config not found: {configPath}");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var runner = document.RootElement.GetProperty("Runner");

        Assert.True(runner.GetProperty("Enabled").GetBoolean());
        Assert.Equal(20, runner.GetProperty("MinFreeGb").GetInt32());
        Assert.True(runner.GetProperty("CleanWorkspaces").GetBoolean());
        Assert.Equal(0, runner.GetProperty("WorkspacesRetentionDays").GetInt32());
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
            if (File.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for GitHub runner housekeeping workflow tests.");
    }
}
