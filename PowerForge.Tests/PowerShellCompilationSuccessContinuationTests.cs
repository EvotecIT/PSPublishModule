using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Transpile_PreservesOutputFreeLocalCallsBeforeTerminalValue()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Setup { [int]$LocalOnly = 1 }; function Get-Result { Invoke-Setup; return 2 }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "SequenceMethods", "net10.0");
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Result");
        Assert.DoesNotContain(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("Non-terminal success output", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Get-Number; Get-Number")]
    [InlineData("if ($Enabled) { Get-Number }; return 2")]
    [InlineData("for ([int]$i = 0; $i -lt 2; $i++) { Get-Number }; return 2")]
    [InlineData("try { Get-Number; return 2 } finally { [int]$Done = 1 }")]
    public void Transpile_RetainsNonTerminalLocalCallOutputInsteadOfReturningEarly(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Number { return 1 }; function Invoke-Sequence { [CmdletBinding()] param([bool]$Enabled) " + body + " }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "SequenceMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Number");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Invoke-Sequence");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("Non-terminal success output", StringComparison.Ordinal));
        Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    public void Build_StrictExecutableRejectsNonTerminalLocalCallOutput()
    {
        using var fixture = ArtifactFixture.Create("function Get-Number { return 1 }; Get-Number; Get-Number");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.SuccessContinuation",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));
        Assert.False(result.Succeeded);
        Assert.Contains("Non-terminal success output", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridRetainedSequencePreservesBothSuccessValues()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Number { return 1 }; function Invoke-Sequence { Get-Number; Get-Number }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.HybridSuccessContinuation",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; @(Invoke-Sequence) -join '|'" );
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("1|1", run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
    }
}
