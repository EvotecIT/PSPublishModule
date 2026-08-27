using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_PackagedExecutablePreservesMyInvocationCommandName()
    {
        using var fixture = ArtifactFixture.Create("$MyInvocation.MyCommand.Name");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedCommandName",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProcess(result.ArtifactPath!);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Path.GetFileName(result.ArtifactPath), run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_PackagedExecutableRejectsConfirmationCapableScripts()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(SupportsShouldProcess = $true)] param(); if ($PSCmdlet.ShouldProcess('target')) { 'changed' }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedConfirmation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("confirmation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsNonBooleanSupportsShouldProcessMetadata()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(SupportsShouldProcess = 1)] param(); 'changed'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedNonBooleanConfirmation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("confirmation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableAdvancesPastNamedArrayForPositionalBinding()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int[]] $Values, [int] $Count); return $Count");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedArrayBinding",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProcess(result.ArtifactPath!, "--Values", "2", "5");
        Assert.Equal((0, "5", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictExecutableValidatesWideFloatingPointRange()
    {
        using var fixture = ArtifactFixture.Create(
            "param([ValidateRange(0, 1e40)] [double] $Value); return $Value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedWideRange",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var accepted = RunProcess(result.ArtifactPath!, "--Value=1e39");
        var rejected = RunProcess(result.ArtifactPath!, "--Value=1e41");
        Assert.Equal(0, accepted.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(accepted.StandardError), accepted.StandardError);
        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("outside the valid range", rejected.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RejectsComputedRequiredManifestFileExpressions()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }", ".psm1");
        var manifest = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(
            manifest,
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FormatsToProcess = @($PSScriptRoot + '/input.format.ps1xml'); FunctionsToExport = @('Get-Value') }");
        File.WriteAllText(Path.Combine(fixture.RootPath, "input.format.ps1xml"), "<Configuration />");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ComputedFormat",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("FormatsToProcess", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_RejectsExplicitManifestThatDoesNotOwnSelectedSource()
    {
        using var fixture = ArtifactFixture.Create("function Get-A { return 1 }", ".psm1");
        var otherSource = Path.Combine(fixture.RootPath, "B.psm1");
        var manifest = Path.Combine(fixture.RootPath, "B.psd1");
        File.WriteAllText(otherSource, "function Get-B { return 2 }");
        File.WriteAllText(manifest, "@{ RootModule = 'B.psm1'; ModuleVersion = '1.0.0' }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.WrongManifest",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifest
        };

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));
        Assert.Contains("does not own", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictManifestRemovesEveryOmittedTypedSourceFromFileList()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RootValue { return 1 }; Export-ModuleMember -Function Get-RootValue",
            ".psm1");
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        var manifest = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(helper, "function Get-Value { return 42 }");
        File.WriteAllText(
            manifest,
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-RootValue', 'Get-Value'); CmdletsToExport = @(); FileList = @('input.psm1', 'Helper.ps1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictFileList",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifest,
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generatedManifest = File.ReadAllText(result.ManifestPath!);
        Assert.DoesNotContain("'input.psm1'", generatedManifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'Helper.ps1'", generatedManifest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PowerForge.StrictFileList.dll", generatedManifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_LocalFunctionCapabilityStillRejectsExternalCommands()
    {
        using var fixture = ArtifactFixture.Create("return Get-Process");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            capabilities: PowerShellCompilationCapability.LocalFunctionCalls));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation);
    }

    [Fact]
    public void Build_StrictExecutableRejectsDependencyParameterBlocks()
    {
        using var fixture = ArtifactFixture.Create(". \"$PSScriptRoot/Helper.ps1\"; return Get-Value");
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "param([Parameter(Mandatory)] [string] $Name); function Get-Value { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DependencyParameters",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("parameter block", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsDotSourceAfterExecutableCode()
    {
        using var fixture = ArtifactFixture.Create("Get-Value; . \"$PSScriptRoot/Helper.ps1\"");
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "function Get-Value { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DependencyOrder",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("appears after executable code", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsReservedEntryPointMethodName()
    {
        using var fixture = ArtifactFixture.Create("function Invoke { return 1 }; return Invoke");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ReservedEntryPoint",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("reserved generated entry-point", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
