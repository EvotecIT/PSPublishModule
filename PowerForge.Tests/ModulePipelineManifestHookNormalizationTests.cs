using System.Reflection;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void ExcludeManifestScriptsToProcess_MatchesCanonicallyEquivalentPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string stagingPath = Path.Combine(root, "staging");
            string hooksPath = Path.Combine(stagingPath, "Hooks");
            Directory.CreateDirectory(hooksPath);
            string composedName = "Résumé.ps1".Normalize(NormalizationForm.FormC);
            string decomposedName = composedName.Normalize(NormalizationForm.FormD);
            Assert.NotEqual(composedName, decomposedName);
            string scriptPath = Path.Combine(hooksPath, decomposedName);
            File.WriteAllText(scriptPath, "'$script:HookRuns++'");
            string manifestPath = Path.Combine(stagingPath, "TestModule.psd1");
            File.WriteAllText(
                manifestPath,
                $"@{{ ModuleVersion = '1.0.0'; ScriptsToProcess = @('Hooks/{composedName}') }}");
            var runner = new ModulePipelineRunner(new NullLogger());
            MethodInfo method = typeof(ModulePipelineRunner).GetMethod(
                "ExcludeManifestScriptsToProcess",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            string[] remaining = Assert.IsType<string[]>(method.Invoke(
                runner,
                new object?[] { manifestPath, stagingPath, new[] { scriptPath } }));

            Assert.Empty(remaining);
            Assert.True(ManifestEditor.TryGetTopLevelStringArray(
                manifestPath,
                "ScriptsToProcess",
                out string[]? scriptsToProcess));
            Assert.Equal(new[] { $"Hooks/{decomposedName}" }, scriptsToProcess);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
