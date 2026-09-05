using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_HybridModuleKeepsConditionalFunctionsOnPowerShellFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "if ($false) { function Get-ConditionalValue { return 1 } }; function Get-TopValue { return 2 }; Export-ModuleMember -Function @('Get-TopValue')");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalFunction",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-ConditionalValue -ErrorAction SilentlyContinue); Get-TopValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleDoesNotTreatConditionalExportAsUnconditional()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HiddenValue { return 1 }; function Get-PublicValue { return 2 }; if ($false) { Export-ModuleMember -Function Get-HiddenValue }; Export-ModuleMember -Function Get-PublicValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-HiddenValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "True", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesConditionalOnlyExportSurface()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HiddenValue { return 1 }; function Get-PublicValue { return 2 }; if ($true) { Export-ModuleMember -Function Get-PublicValue }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalOnlyExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-HiddenValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "True", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesDefaultExportsWhenConditionalOnlyExportDoesNotRun()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FirstValue { return 1 }; function Get-SecondValue { return 2 }; if ($false) { Export-ModuleMember -Function Get-SecondValue }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalFalseExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-FirstValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-SecondValue -ErrorAction SilentlyContinue); Get-FirstValue; Get-SecondValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "True", "True", "1", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesColonAttachedLiteralExport()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PublicValue { return 1 }; function Get-PrivateValue { return 2 }; Export-ModuleMember -Function:Get-PublicValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AttachedExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "True", "False", "1" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }
}
