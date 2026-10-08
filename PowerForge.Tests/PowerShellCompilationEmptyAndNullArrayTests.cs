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
            function Test-EmptyIdentity { [CmdletBinding()] param() $First = @(); $Second = @(); return [object]::ReferenceEquals($First, $Second) }
            function Test-TypedEmptyIdentity { [CmdletBinding()] param() [int[]]$First = @(); [int[]]$Second = @(); return [object]::ReferenceEquals($First, $Second) }
            function Get-EmptyJaggedCount { [CmdletBinding()] param() [int[][]]$Values = @(); return $Values.Length }
            function Get-EmptyDeepJaggedCount { [CmdletBinding()] param() [string[][][]]$Values = @(); return $Values.Length }
            function Test-EmptyConcatenationIdentity { [CmdletBinding()] param() $First = @() + @(); $Second = @() + @(); return [object]::ReferenceEquals($First, $Second) }
            function Get-EmptyCount { [CmdletBinding()] param() $Values = @(); return $Values.Length }
            function Get-NullCount { [CmdletBinding()] param() $Values = @($null); return $Values.Length }
            function Get-MixedNullCount { [CmdletBinding()] param() $Values = @(1; $null; 2); return $Values.Length }
            function Get-CollectedNull { [CmdletBinding()] param() $Values = @($null); return $Values[0] }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ClosedArrays",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(9, result.Manifest!.CompiledMethods);
        const string probe = "Test-EmptyIdentity; Test-TypedEmptyIdentity; Get-EmptyCount; Get-NullCount; Get-MixedNullCount; $null -eq (Get-CollectedNull); Get-EmptyJaggedCount; Get-EmptyDeepJaggedCount; Test-EmptyConcatenationIdentity";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal((0, "False|False|0|1|3|True|0|0|" + (framework == "net472" ? "False" : "True"), string.Empty),
            (original.ExitCode, string.Join("|", original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)), original.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("int", "@($null)")]
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

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Item = $null; [int]$Count = 0; foreach ($Item in ,(1,2)) { $Values = @($Item); $Count += $Values.Length }; return $Count")]
    [InlineData("[object]$Item = $null; [int]$Count = 0; [int]$Index = 0; while ($Index -lt 2) { $Values = @($Item); $Count += $Values.Length; $Item = 1,2; $Index += 1 }; return $Count")]
    [InlineData("[object]$Item = $null; [int]$Count = 0; for ([int]$Index = 0; $Index -lt 2; $Index++) { $Values = @($Item); $Count += $Values.Length; $Item = 1,2 }; return $Count")]
    [InlineData("[object]$Item = $null; [int]$Count = 0; [int]$Index = 0; do { $Values = @($Item); $Count += $Values.Length; $Item = 1,2; $Index += 1 } while ($Index -lt 2); return $Count")]
    public void Transpile_CollectionDoesNotTrustStaleLoopNullState(string body)
    {
        using var fixture = ArtifactFixture.Create("function Get-LoopCollection { " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "LoopCollectionMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-LoopCollection");
    }
}
