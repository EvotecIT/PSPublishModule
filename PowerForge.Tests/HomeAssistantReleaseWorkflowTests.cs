namespace PowerForge.Tests;

public sealed class HomeAssistantReleaseWorkflowTests {
    [Fact]
    public void WorkflowTransfersZipAssetsFromTheHiddenPowerForgeDirectory() {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var workflowPath = Path.Combine(root, ".github", "workflows", "powerforge-homeassistant-release.yml");
        var workflow = File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var uploadStepStart = workflow.IndexOf("- name: Transfer the single release asset to the publish job", StringComparison.Ordinal);
        var publishJobStart = workflow.IndexOf("\n  publish:", uploadStepStart, StringComparison.Ordinal);
        var uploadStep = workflow[uploadStepStart..publishJobStart];

        Assert.Contains("uses: actions/upload-artifact@", uploadStep, StringComparison.Ordinal);
        Assert.Contains("path: ${{ steps.build.outputs.asset-path }}", uploadStep, StringComparison.Ordinal);
        Assert.Contains("include-hidden-files: true", uploadStep, StringComparison.Ordinal);
    }
}
