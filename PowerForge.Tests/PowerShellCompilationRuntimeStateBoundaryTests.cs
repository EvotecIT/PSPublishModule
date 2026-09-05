using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_ForeachWhatIfPreferenceIsNotReplacedByHostState()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LoopPreference { [CmdletBinding(SupportsShouldProcess = $true)] param([bool[]] $Flags) " +
            "foreach ($WhatIfPreference in $Flags) { if ($WhatIfPreference) { return $true } }; return $false }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.LoopWhatIf", "CompiledPowerShell", "net8.0");
        var method = Assert.Single(typed.Methods);
        Assert.False(method.RequiresPowerShellRuntimeState);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LoopWhatIf",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Get-LoopPreference -Flags $false -WhatIf");
        Assert.Equal((0, "False", string.Empty), (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Fact]
    public void Analyze_VersionTableMemberMutationRemainsOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-VersionState { $PSVersionTable.PSVersion = [Version] '1.0'; return $PSVersionTable.PSVersion }",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("$env:POWERFORGE_RUNTIME_STATE_PROOF = 'changed'; return $env:POWERFORGE_RUNTIME_STATE_PROOF")]
    [InlineData("$script:Cache = @{ Name = 'changed' }; return $script:Cache")]
    [InlineData("$global:Preference = 'changed'; return $global:Preference")]
    public void Analyze_RuntimeOwnedScopeMutationFailsClosed(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Set-RuntimeOwnedState {{ {body} }}", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Fact]
    public void Analyze_ErrorSnapshotMutationFailsClosed()
    {
        using var fixture = ArtifactFixture.Create("function Clear-Errors { $Error.Clear(); return $Error.Count }", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.Message.Contains("read-only invocation snapshot", StringComparison.Ordinal));
    }
}
