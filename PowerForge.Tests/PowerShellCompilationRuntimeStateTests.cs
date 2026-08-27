using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_StrictExecutableLowersTargetAndPlatformStateWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "return (($PSEdition -eq 'Core') -and $IsCoreCLR -and ($IsWindows -or $IsLinux -or $IsMacOS))");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var run = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal((0, "True", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesEditionAndVersionState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-EditionState { return $PSEdition }; " +
            "function Get-VersionState { return $PSVersionTable.PSVersion.ToString() }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = targetFramework
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        const string calls = "Get-EditionState; Get-VersionState";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesShouldProcessAndWhatIfState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Test-RuntimeApproval { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return $PSCmdlet.ShouldProcess($Target, 'Change') }; " +
            "function Set-RuntimeState { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) " +
            "if ($WhatIfPreference) { return 'whatif' }; if ($PSCmdlet.ShouldProcess($Target, 'Change')) { return 'changed' }; return 'skipped' }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.RuntimeStateModule",
            "CompiledPowerShell",
            targetFramework);
        Assert.Equal(2, typed.Methods.Length);
        Assert.All(typed.Methods, static method => Assert.True(method.RequiresPowerShellRuntimeState));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = targetFramework
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        foreach (var command in new[]
                 {
                     "Set-RuntimeState -Target 'item' -Confirm:$false",
                     "Set-RuntimeState -Target 'item' -WhatIf",
                     "$global:WhatIfPreference = $true; Set-RuntimeState -Target 'item' -WhatIf:$false"
                 })
        {
            var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
                $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
            var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
                $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");

            Assert.Equal(0, original.ExitCode);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
            Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        }
    }

    [Fact]
    public void Analyze_StrictExecutableKeepsPSCmdletInteractionOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return $PSCmdlet.ShouldProcess($Target)");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));

        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
        Assert.Contains(Assert.Single(plan.Files).Units.SelectMany(static unit => unit.Diagnostics), static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("return $PSVersionTable.GitCommitId")]
    [InlineData("return $PSCmdlet.ShouldContinue('Continue?', 'Caption')")]
    public void Analyze_RuntimeStateIntrinsicsRemainBounded(string body)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-RuntimeState {{ [CmdletBinding(SupportsShouldProcess = $true)] param() {body} }}",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units, static unit => unit.Kind == PowerShellCompilationUnitKind.Function);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Fact]
    public void Transpile_TypedLibraryLowersStaticRuntimeFacts()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StaticFact { if ($IsWindows) { return $PSEdition + ':Windows' }; return $PSEdition + ':Other' }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().Transpile(
            fixture.ScriptPath,
            "PowerForge.StaticFacts",
            "CompiledPowerShell",
            "net8.0");

        Assert.True(typed.Methods.Length == 1, string.Join(Environment.NewLine, typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.False(typed.Methods[0].RequiresPowerShellRuntimeState);
        Assert.Contains("Core", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LocalWhatIfPreferenceAssignmentIsNotReplacedByHostState()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LocalPreference { [CmdletBinding(SupportsShouldProcess = $true)] param() " +
            "$WhatIfPreference = $false; return $WhatIfPreference }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.LocalWhatIf", "CompiledPowerShell", "net8.0");
        var method = Assert.Single(typed.Methods);
        Assert.False(method.RequiresPowerShellRuntimeState);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LocalWhatIf",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Get-LocalPreference -WhatIf");
        Assert.Equal((0, "False", string.Empty), (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

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
            PowerShellCompilationMode.Strict));
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
}
