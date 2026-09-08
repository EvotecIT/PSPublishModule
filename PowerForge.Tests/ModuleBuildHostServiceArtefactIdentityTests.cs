using System.Reflection;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBuildHostServiceArtefactIdentityTests
{
    [Fact]
    public void DistinctArtefactOutputs_KeepsSharedScriptRootEntriesDistinctByEntryPoint()
    {
        string outputRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var first = new PowerForgeModuleArtefactOutputSummary
        {
            Type = ArtefactType.Script,
            OutputPath = outputRoot,
            EntryPointRelativePath = "first/Invoke-One.ps1"
        };
        var second = new PowerForgeModuleArtefactOutputSummary
        {
            Type = ArtefactType.Script,
            OutputPath = outputRoot,
            EntryPointRelativePath = "second/Invoke-Two.ps1"
        };
        var repeatedFirst = new PowerForgeModuleArtefactOutputSummary
        {
            Type = ArtefactType.Script,
            OutputPath = outputRoot,
            EntryPointRelativePath = "first\\Invoke-One.ps1"
        };
        MethodInfo? method = typeof(ModuleBuildHostService).GetMethod(
            "DistinctArtefactOutputs",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var outputs = Assert.IsType<PowerForgeModuleArtefactOutputSummary[]>(
            method!.Invoke(null, new object[] { new[] { first, second, repeatedFirst } }));

        Assert.Collection(
            outputs,
            output => Assert.Equal("first/Invoke-One.ps1", output.EntryPointRelativePath),
            output => Assert.Equal("second/Invoke-Two.ps1", output.EntryPointRelativePath));
    }
}
