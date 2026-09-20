namespace PowerForge.Tests;

public sealed class ModuleBuildOnlyInvocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildOnlyLegacyScriptRequiresControlsBeforeItCanRun(bool supported)
    {
        var root = Path.Combine(Path.GetTempPath(), "module-build-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var module = Path.Combine(root, "Fixture.psm1");
            var script = Path.Combine(root, "Build-Module.ps1");
            var marker = Path.Combine(root, "invocation.txt");
            await File.WriteAllTextAsync(module, "# Empty module fixture");
            var parameters = supported
                ? "param([string]$RunMode, [switch]$NoSign, [switch]$SkipInstall, [bool]$IncludeModulePublishing, [string]$StagingPath)"
                : "param()";
            await File.WriteAllTextAsync(script, parameters + "\nSet-Content -LiteralPath (Join-Path $PSScriptRoot 'invocation.txt') -Value \"$RunMode|$NoSign|$SkipInstall|$IncludeModulePublishing\"");
            var result = await new ModuleBuildHostService().ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
                RepositoryRoot = root, ModulePath = module, ScriptPath = script,
                RunMode = ConfigurationGateMode.Build, NoSign = true, SkipInstall = true,
                IncludeModulePublishing = false, RequireBuildOnly = true
            });
            if (supported)
            {
                Assert.True(result.Succeeded, result.StandardError);
                Assert.Equal("Build|True|True|False", (await File.ReadAllTextAsync(marker)).Trim());
            }
            else
            {
                Assert.False(result.Succeeded);
                Assert.Contains("Build-only execution requires", result.StandardError);
                Assert.False(File.Exists(marker));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
