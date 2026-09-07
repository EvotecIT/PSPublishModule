using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_ClosedArrayValuesPreserveIdentityAndNullCardinality(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Test-EmptyIdentity { $First = @(); $Second = @(); return [object]::ReferenceEquals($First, $Second) }
            function Test-TypedEmptyIdentity { [int[]]$First = @(); [int[]]$Second = @(); return [object]::ReferenceEquals($First, $Second) }
            function Get-EmptyCount { $Values = @(); return $Values.Length }
            function Get-NullCount { $Values = @($null); return $Values.Length }
            function Get-MixedNullCount { $Values = @(1; $null; 2); return $Values.Length }
            function Get-CollectedNull { $Values = @($null); return $Values[0] }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ClosedArrays",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(6, result.Manifest!.CompiledMethods);
        const string probe = "Test-EmptyIdentity; Test-TypedEmptyIdentity; Get-EmptyCount; Get-NullCount; Get-MixedNullCount; $null -eq (Get-CollectedNull)";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal((0, "False|False|0|1|3|True", string.Empty),
            (original.ExitCode, string.Join("|", original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)), original.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("string", "@($null)")]
    [InlineData("int", "@($null)")]
    [InlineData("string", ",$null")]
    [InlineData("int", ",$null")]
    public void Transpile_TypedNullCollectionRetainsPowerShellConversion(string elementType, string expression)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedNull { [" + elementType + "[]]$Values = " + expression + "; return $Values }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "TypedNullMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-TypedNull");
    }
}
