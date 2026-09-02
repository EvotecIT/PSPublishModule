using System.Runtime.InteropServices;
using System.Management.Automation.Language;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModuleCapturesCommandResultsAndResumesTypedCode(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-CapturedScalar { [CmdletBinding()] param([string] $Value) [int] $count = 1; [string] $captured = Write-Output $Value; " +
            "$count += 1; if ($captured -eq $Value) { return $count }; return 0 }; " +
            "function Get-CapturedArrayLength { [CmdletBinding()] param([string[]] $Values) [string[]] $captured = Write-Output $Values; return $captured.Length }; " +
            "function Get-CapturedNullLength { [CmdletBinding()] param(); [string] $captured = Write-Output -InputObject $null; return $captured.Length }; " +
            "function Get-CapturedHelper { [CmdletBinding()] param([string] $InputText) [string] $captured = Write-Output $InputText; return $captured }; " +
            "function Get-CapturedOuter { [CmdletBinding()] param([string] $InputText) return Get-CapturedHelper -InputText $InputText }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.CommandCapture",
            "CompiledPowerShell",
            targetFramework);
        Assert.True(typed.Diagnostics.Length == 0, string.Join(Environment.NewLine, typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(5, typed.Methods.Length);
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null, targetFramework);
        Assert.True(prepared.Diagnostics.Length == 0, string.Join(Environment.NewLine, prepared.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: targetFramework,
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        Assert.True(plan.CanProceed, string.Join(Environment.NewLine, plan.Files.SelectMany(static file =>
            file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics))).Select(static diagnostic => diagnostic.Message)));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommandCapture",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            EmitSource = true
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        const string calls = "Get-CapturedScalar -Value 'Ada'; Get-CapturedArrayLength -Values @('a','b','c'); Get-CapturedNullLength; Get-CapturedOuter -InputText 'Grace'";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("__invokePowerShellCapture", generated, StringComparison.Ordinal);
        Assert.Contains("Get_CapturedHelper(", generated, StringComparison.Ordinal);
        Assert.Contains("count = checked((int)(count + 1))", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_CommandCaptureRequiresAnExplicitTypedTarget()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UntypedCapture { [int] $count = 1; $captured = Write-Output 'value'; $count += 1; return $count }",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units, static unit => unit.Kind == PowerShellCompilationUnitKind.Function);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation ||
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Fact]
    public void Build_CommandCapturePreservesExtendedTypeSystemProperties()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ExtendedObject { [CmdletBinding()] param() " +
            "[object] $captured = [object]::new() | Add-Member -NotePropertyName Name -NotePropertyValue Ada -PassThru; return $captured }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExtendedCapture",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; (Get-ExtendedObject).Name");
        Assert.Equal((0, "Ada", string.Empty), (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Fact]
    public void Build_CommandCaptureUnwrapsOrdinaryValuesForTypedClrOperations()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-CapturedTypes { [CmdletBinding()] param() " +
            "[object] $scalar = Write-Output 1; [object[]] $items = Write-Output 2, 3; " +
            "foreach ($item in $items) { if (-not ($item -is [int])) { return $false } }; return ($scalar -is [int]) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ScalarCapture",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var original = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; Test-CapturedTypes");
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Test-CapturedTypes");

        Assert.Equal((0, "True", string.Empty), (original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [InlineData("Write-Output $later; [string] $later = 'value'; return $later")]
    [InlineData("[string] $captured = Write-Output $later; [string] $later = 'value'; return $captured")]
    public void Analyze_CommandRegionCannotCaptureFutureLocal(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Get-FutureLocal {{ {body} }}", ".psm1");
        var ast = Parser.ParseFile(fixture.ScriptPath, out _, out _);
        var function = Assert.Single(ast.FindAll(static node => node is FunctionDefinitionAst, false).Cast<FunctionDefinitionAst>());
        var statement = function.Body.EndBlock!.Statements[0];
        var allowed = new HashSet<string>(new[] { "captured", "later" }, StringComparer.OrdinalIgnoreCase);
        var resolver = new PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry.Default);

        if (statement is AssignmentStatementAst)
            Assert.False(PowerShellCommandIslandPolicy.TryGetCapturedRuntimeAssignment(
                statement,
                function.Body,
                localFunctionNames: null,
                allowed,
                PowerShellCompilationCapabilities.BinaryModule,
                resolver,
                out _));
        else
            Assert.False(PowerShellCommandIslandPolicy.IsRuntimeRegion(
                statement,
                function.Body,
                localFunctionNames: null,
                allowed,
                PowerShellCompilationCapabilities.BinaryModule,
                resolver));
    }
}
