using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Analyze_RuntimeFreeLibraryRejectsMixedScalarReturnTypes()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-MixedRecord { param([int] $Count, [switch] $Text) if ($Text) { return 'text' }; return $Count }
            """, ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Strict,
            capabilities: PowerShellCompilationCapabilities.TypedLibrary));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("branch-specific runtime types", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridModulePreservesReachableScalarReturnTypes(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-MixedRecord {
                [CmdletBinding()]
                param([int] $Count, [switch] $Text)
                if ($null -eq $Count -or $Text) { return 'text' }
                return $Count
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ScalarReturnUnion",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.True(Assert.Single(result.Manifest.UnitDispositionLedger!.Entries).EmittedClrMethod);

        const string probe = """
            foreach ($argsHash in @(@{Count=7}, @{Count=7;Text=$true}, @{}, @{Count=$null})) {
                $values = @(Get-MixedRecord @argsHash)
                '{0}:{1}:{2}' -f $values.Count,$values[0].GetType().FullName,$values[0]
            }
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("1:System.Int32:7", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("1:System.String:text", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }
}
