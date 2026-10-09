namespace PowerForge.Tests;

public sealed class HomeAssistantReleaseWorkflowTests {
    [Fact]
    public void WorkflowTransfersZipAssetsFromTheHiddenPowerForgeDirectory() {
        var root = RepoRootLocator.Find();
        var workflowPath = Path.Combine(root, ".github", "workflows", "powerforge-homeassistant-release.yml");
        var workflow = File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var uploadStepStart = workflow.IndexOf("- name: Transfer the single release asset to the publish job", StringComparison.Ordinal);
        var publishJobStart = workflow.IndexOf("\n  publish:", uploadStepStart, StringComparison.Ordinal);
        var uploadStep = workflow[uploadStepStart..publishJobStart];

        Assert.Contains("uses: actions/upload-artifact@", uploadStep, StringComparison.Ordinal);
        Assert.Contains("path: ${{ steps.build.outputs.asset-path }}", uploadStep, StringComparison.Ordinal);
        Assert.Contains("include-hidden-files: true", uploadStep, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowPinsThePublishedReleaseEngineVersionForEveryStage() {
        var root = RepoRootLocator.Find();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "powerforge-homeassistant-release.yml"));
        var action = File.ReadAllText(Path.Combine(root, ".github", "actions", "homeassistant-release", "action.yml"));

        Assert.Equal(3, CountOccurrences(workflow, "powerforge-version: 3.0.154"));
        Assert.Contains("actions: read", workflow, StringComparison.Ordinal);
        Assert.Contains("default: \"3.0.154\"", action, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowAndReceiverTreatPullRequestNumbersAsTextualIdentifiers() {
        var root = RepoRootLocator.Find();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "powerforge-homeassistant-release.yml"));

        Assert.Contains("pr_number:\n        description: Merged pull request number that initiated the release.\n        required: true\n        type: string", workflow.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("pr-number: ${{ inputs.pr_number }}", workflow, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search) {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += search.Length;
        }

        return count;
    }
}
