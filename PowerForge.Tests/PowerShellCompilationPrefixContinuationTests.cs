using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Number += 1")]
    [InlineData("$Number++")]
    [InlineData("$Number %= 0")]
    public void Transpile_HybridTerminalGraphIncludesNumericMutationFailures(string mutation)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NumericRegion { [CmdletBinding()] param([int]$Number); " +
            "data HostedData { 'before' }; " + mutation + "; return $Number }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericRegionMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(typed.PromotedRegions);
        var candidate = Assert.Single(typed.RegionCandidates);
        Assert.Equal("region.error-route", candidate.DecisionCode);
        Assert.Contains("ClrException", Assert.Single(candidate.RegionGraph!.Regions).Errors);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh", "[int]", "System.Int32")]
    [InlineData("net10.0", "pwsh", "", "System.String")]
    [InlineData("net472", "powershell.exe", "[int]", "System.Int32")]
    public void Build_HybridPrefixResumesWithSameOutputAndVariableConstraint(
        string framework, string host, string constraint, string finalType)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-PrefixValue {
                [CmdletBinding()]
                param([bool] $Enabled)
                CONSTRAINT $result = 0
                if ($Enabled) { $result = 7 }
                & { "hosted:$result" }
                $result = '9'
                "after:$($result.GetType().FullName):$result"
            }
            Export-ModuleMember -Function Get-PrefixValue
            """.Replace("CONSTRAINT", constraint, StringComparison.Ordinal), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.PrefixContinuation",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.PromotedTypedRegions > 0);
        const string probe = "@(Get-PrefixValue $false; Get-PrefixValue $true) -join '|'";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("hosted:0|after:" + finalType + ":9|hosted:7|after:" + finalType + ":9", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("[int]$result = 0; $result += $Number; & { $result }")]
    [InlineData("[int]$result = 0; $Number = 7; & { $result; $Number }")]
    [InlineData("[int]$result = 0; Write-Warning 'before'; $result = 7; & { $result }")]
    [InlineData("& { 'before' }; [int]$result = 0; $result = 7; & { $result }")]
    public void Transpile_HybridPrefixRejectsUnrepresentedFailuresAndSideEffects(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PrefixValue { [CmdletBinding()] param([int]$Number); " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "PrefixMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.PromotedRegions, region => region.ContinuationVariable.Length != 0);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridPrefixTransfersScalarAndRetainsContinuation()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-PrefixValue {
                [CmdletBinding()]
                param([bool] $Enabled)
                [int] $result = 0
                if ($Enabled) { $result = 7 }
                & { "hosted:$result" }
                return $result
            }
            Export-ModuleMember -Function Get-PrefixValue
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "PrefixMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var prefix = Assert.Single(typed.PromotedRegions, region => region.ContinuationVariable.Length != 0);
        Assert.Equal("result", prefix.ContinuationVariable, ignoreCase: true);
        Assert.Equal("System.Int32", prefix.ContinuationTypeConstraint);
        Assert.Equal(new[] { "Enabled" }, prefix.InputParameters.Select(parameter => parameter.Name));
        Assert.Empty(Assert.Single(prefix.RegionGraph.Regions).Errors);
        Assert.DoesNotContain("hosted:", typed.SourceCode, StringComparison.Ordinal);
    }
}
