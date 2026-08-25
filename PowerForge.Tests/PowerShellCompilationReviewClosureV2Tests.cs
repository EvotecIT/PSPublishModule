using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_HybridModuleKeepsShouldProcessLocalCallerOnCommandPath()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-InnerChange { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return $PSCmdlet.ShouldProcess($Target) }; " +
            "function Invoke-OuterChange { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return Invoke-InnerChange -Target $Target }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.ShouldProcessIdentity",
            "CompiledPowerShell",
            "net8.0");

        Assert.Contains(typed.Methods, static method => method.SourceName == "Invoke-InnerChange");
        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Invoke-OuterChange");
        Assert.Contains(typed.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("command identity", StringComparison.OrdinalIgnoreCase));

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ShouldProcessIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var original = Run(
            "pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; Invoke-OuterChange -Target item -WhatIf");
        var compiled = Run(
            "pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Invoke-OuterChange -Target item -WhatIf");

        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.Contains("Invoke-InnerChange", original.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-InnerChange", compiled.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-OuterChange", compiled.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Transpile_RuntimeStateSelfRecursionRemainsOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-RecursiveChange { [CmdletBinding(SupportsShouldProcess = $true)] [OutputType([bool])] " +
            "param([long] $Number, [string] $Target) if ($Number -le [long] 0) { return $PSCmdlet.ShouldProcess($Target) }; " +
            "$Number -= [long] 1; return Invoke-RecursiveChange -Number $Number -Target $Target }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.RuntimeStateRecursion",
            "CompiledPowerShell",
            "net8.0");

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("ShouldProcess", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("PowerShell command path", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("[object]", "return $PSVersionTable.PSVersion")]
    [InlineData("[bool]", "return $WhatIfPreference")]
    public void Build_RuntimeStateSelfRecursionPassesHostArguments(string outputType, string terminalExpression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-RecursiveState {{ [CmdletBinding(SupportsShouldProcess = $true)] [OutputType({outputType})] " +
            "param([long] $Number) if ($Number -le [long] 0) { " + terminalExpression + " }; " +
            "$Number -= [long] 1; return Get-RecursiveState -Number $Number }",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateRecursion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
    }

    [Fact]
    public void Build_RuntimeStateAllowsFormerWhatIfTemporaryAsParameter()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RuntimeState { [CmdletBinding(SupportsShouldProcess = $true)] " +
            "param([string] $__boundWhatIf) if ($WhatIfPreference) { return $__boundWhatIf }; return 'none' }",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.WhatIfParameter",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
    }

    [Fact]
    public void Analyze_StrictExecutableRejectsSwitchTypeExpressionsWithoutSmaReference()
    {
        using var fixture = ArtifactFixture.Create("return 'value' -is [switch]");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("statically resolvable CLR type", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("InvokePowerShellRegion")]
    [InlineData("CapturePowerShellRegion")]
    public void Prepare_BinaryModuleRejectsCommandRegionHelperParameterCollision(string parameterName)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-ReservedCapture {{ [CmdletBinding()] param([string] ${parameterName}, [string] $Value) " +
            "[string] $captured = Write-Output $Value; return $captured }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.RegionCollision",
            "CompiledPowerShell",
            "net8.0");

        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
            typed,
            exportedFunctions: null,
            "net8.0");

        Assert.Empty(prepared.Methods);
        Assert.Contains(prepared.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(parameterName, StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("generated or inherited binary-cmdlet member", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TypedLocalDeclarationUsesRequestedTargetFramework()
    {
        using var fixture = ArtifactFixture.Create(
            "function New-TargetLock { [System.Threading.Lock] $Value = $null; return $Value }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0"));
        var function = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.Conversion);
    }
}
