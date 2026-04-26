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
        Assert.Contains("PowerForge GitHub Runner Housekeeping", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner-labels", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("fromJson(inputs.runner_labels_json", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("inputs['runner-labels']", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("[\"self-hosted\",\"ubuntu\"]", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("./.powerforge/pspublishmodule/.github/actions/github-housekeeping", workflowYaml, StringComparison.Ordinal);
        Assert.Contains(".powerforge/runner-housekeeping.json", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("runner-min-free-gb", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("report-artifact-name", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflowYaml, StringComparison.Ordinal);
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
