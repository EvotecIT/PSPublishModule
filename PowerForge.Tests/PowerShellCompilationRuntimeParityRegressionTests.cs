using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_StrictLibraryReturnsNullWhenIndexingNullDictionary()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DictionaryValue { param([System.Collections.IDictionary] $Map) return $Map['key'] }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullDictionary",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes()
            .SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_DictionaryValue");
        Assert.Null(method.Invoke(null, new object?[] { null }));
        Assert.Equal(9, method.Invoke(null, new object?[] { new System.Collections.Hashtable { ["key"] = 9 } }));
    }

    [Fact]
    public void Build_StrictBinaryModuleSimpleFunctionAcceptsSurplusArguments()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SimpleValue { param([int] $Value) return $Value } Export-ModuleMember -Function Get-SimpleValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SimpleArguments",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("7", RuntimeParityRunModuleProof(result.ArtifactPath!, "Get-SimpleValue 7 ignored"));
    }

    [Fact]
    public void Build_HybridScalarizesArrayReturningLocalFunctionWhenConsumed()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-One { return @('x') } " +
            "function Get-ConsumedType { $value = Get-One; return $value.GetType().FullName } " +
            "Export-ModuleMember -Function Get-One, Get-ConsumedType",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ArrayLocalCall",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("pipeline cardinality", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("System.String", RuntimeParityRunModuleProof(result.ArtifactPath!, "Get-ConsumedType"));
    }

    [Fact]
    public void Build_HybridPreservesObservableSwitchParameterIdentity()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SwitchType { param([switch] $Force) return $Force.GetType().FullName } " +
            "function Get-SafeSwitch { param([switch] $Force) if ($Force) { return 1 }; return 0 } " +
            "Export-ModuleMember -Function Get-SwitchType, Get-SafeSwitch",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("SwitchParameter", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { "System.Management.Automation.SwitchParameter", "1" },
            RuntimeParityRunModuleProof(result.ArtifactPath!, "Get-SwitchType -Force; Get-SafeSwitch -Force")
                .Split(Environment.NewLine));
    }

    [Fact]
    public void Analyze_RejectsSideEffectingIndexedArrayLiteralBeforeEmission()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-IndexedValue { return ([System.Guid]::NewGuid(), [System.Guid]::Empty)[0] }");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer()
            .Analyze(new PowerShellCompilationSpec(fixture.ScriptPath)).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("side-effect-free", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_HybridDoesNotExportDispatcherVariablesThroughWildcard()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Region { param([int] $Value) [int] $result = $Value; " +
            "$null = Write-Output 'hidden'; Write-Output 'visible'; $result += 1; return $result } " +
            "Export-ModuleMember -Function Invoke-Region -Variable *",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DispatcherVariables",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"$m = Import-Module -Name '{escapedPath}' -Force -PassThru; " +
            "$names = ($m.ExportedVariables.Keys | Sort-Object) -join ','; " +
            "$value = (Invoke-Region -Value 1) -join '|'; Remove-Module $m; \"$names;$value\"");
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("__powerForge", run.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(";visible|2", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridPreservesCrossFileDeclarationTiming()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Safe { return 1 } Get-Later; . \"$PSScriptRoot/Later.ps1\"; " +
            "Export-ModuleMember -Function Get-Safe, Get-Later",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Later.ps1"), "function Get-Later { return 7 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CrossFileTiming",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"$importOutput = @(& {{ Import-Module -Name '{escapedPath}' -Force }} 2>$null); " +
            "\"import=$($importOutput.Count);later=$([bool](Get-Command Get-Later -ErrorAction SilentlyContinue))\"");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("import=0;later=False", run.StandardOutput.Trim());
    }

    private static string RuntimeParityRunModuleProof(string modulePath, string command)
    {
        var escapedPath = modulePath.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; {command}");
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        return run.StandardOutput.Trim();
    }
}
