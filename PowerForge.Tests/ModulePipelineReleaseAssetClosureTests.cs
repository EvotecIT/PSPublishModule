namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void CollectModuleReleaseAssets_SelectsNestedScriptEntryPointAndEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string scriptPath = Path.Combine(root, "app", "Invoke-Sample.ps1");
        string evidencePath = Path.Combine(root, "PowerForge.ScriptEvidence.json");
        var artefact = new ArtefactBuildResult(
            ArtefactType.Script,
            "release-script",
            root,
            Array.Empty<ArtefactModuleEntry>(),
            Array.Empty<ArtefactCopyEntry>(),
            new[] { evidencePath },
            "app/Invoke-Sample.ps1");
        var method = typeof(ModulePipelineRunner).GetMethod(
            "CollectModuleReleaseAssets",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        string[] assets = Assert.IsType<string[]>(method!.Invoke(
            null,
            new object?[] { new[] { artefact }, "release-script" }));

        Assert.Equal(
            new[] { Path.GetFullPath(scriptPath), Path.GetFullPath(evidencePath) },
            assets);
    }
}
