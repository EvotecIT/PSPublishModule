using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("param([int[]] $Values); [int[]] $copy = @($Values); return $copy.Length")]
    [InlineData("[string[]] $copy = @($null); return $copy.Length")]
    public void Analyze_RoutesArraySubexpressionPipelineSemanticsToFallback(string body)
    {
        using var fixture = ArtifactFixture.Create("function Get-Copy { " + body + " }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("pipeline output", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsExitInDotSourceDependency()
    {
        using var fixture = ArtifactFixture.Create(". \"$PSScriptRoot/Helper.ps1\"; return 1");
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "exit 7");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DependencyExit",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("dependency exits", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("@('Get-NestedValue')")]
    [InlineData("@('Get-*')")]
    public void Build_HybridModulePreservesCmdletExportFromNestedBinaryModule(string cmdletsToExport)
    {
        using var nestedFixture = ArtifactFixture.Create("function Get-NestedValue { return 9 }", ".psm1");
        var nestedResult = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            nestedFixture.ScriptPath,
            nestedFixture.OutputPath,
            "PowerForge.NestedLiteralCmdlet",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));
        Assert.True(nestedResult.Succeeded, nestedResult.Error + Environment.NewLine + nestedResult.BuildOutput);

        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 1 }; Export-ModuleMember -Function Get-TypedValue",
            ".psm1");
        File.Copy(nestedResult.ArtifactPath!, Path.Combine(fixture.RootPath, "NestedLiteralCmdlet.dll"));
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            $"@{{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('NestedLiteralCmdlet.dll'); FunctionsToExport = @('Get-TypedValue'); CmdletsToExport = {cmdletsToExport} }}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LiteralNestedExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "9" }, RunModuleProof(result.ArtifactPath!, "Get-TypedValue; Get-NestedValue").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_HybridModulePreservesMixedConditionalAndLiteralExports()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DirectValue { return 1 }; function Get-ConditionalValue { return (Get-Date).Year }; " +
            "Export-ModuleMember -Function Get-DirectValue; if ($true) { Export-ModuleMember -Function Get-ConditionalValue }",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-DirectValue', 'Get-ConditionalValue'); CmdletsToExport = @() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MixedRuntimeExports",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var proof = RunModuleProof(result.ArtifactPath!, "Get-DirectValue; [int](Get-ConditionalValue) -gt 2000");
        Assert.Equal(new[] { "1", "True" }, proof.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_HybridModulePreservesEmptyConventionalLoaderDirectory()
    {
        using var fixture = ArtifactFixture.Create(
            "$ErrorActionPreference = 'Stop'; $Public = @(Get-ChildItem -Path $PSScriptRoot/Public/*.ps1); " +
            "foreach ($File in $Public) { . $File.FullName }; function Get-TypedValue { return 1 }; Export-ModuleMember -Function Get-TypedValue",
            ".psm1");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Public"));
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-TypedValue'); CmdletsToExport = @() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.EmptyLoaderDirectory",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "Public")));
        Assert.Equal("1", RunModuleProof(result.ArtifactPath!, "Get-TypedValue"));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesNullArrayIndexFailure()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierArrayValue { param([int[]] $Values); return $Values[0] }; " +
            "Export-ModuleMember -Function Get-FrontierArrayValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullArrayIndex",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "try { Get-FrontierArrayValue -ErrorAction Stop; 'unexpected' } catch { $_.Exception.Message }");
        Assert.Contains("Cannot index into a null array", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[double]::NaN -eq [double]::NaN", "False")]
    [InlineData("[double]::NaN -ne [double]::NaN", "True")]
    [InlineData("[float]::NaN -eq [float]::NaN", "False")]
    [InlineData("[float]::NaN -ne [float]::NaN", "True")]
    public void Build_StrictExecutablePreservesPowerShellNaNEquality(string expression, string expected)
    {
        using var fixture = ArtifactFixture.Create("return " + expression);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NaNEquality",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!);
        Assert.Equal((0, expected, string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
    }

    [Fact]
    public void PathComparison_DetectsTheCurrentVolumeCaseBehavior()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var probe = Path.Combine(root, "CaseProbe");
        Directory.CreateDirectory(probe);
        try
        {
            var alternate = Path.Combine(root, "caseProbe");
            var expected = Directory.Exists(alternate)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            Assert.Equal(expected, PowerShellCompilationPathSafety.GetPathComparison(probe));
            if (expected == StringComparison.Ordinal)
            {
                Directory.CreateDirectory(alternate);
                Assert.Equal(2, new[] { probe, alternate }.Distinct(PowerShellCompilationPathSafety.PathComparer).Count());
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, false, "linux-musl-x64", Architecture.X64, "linux-musl-x64")]
    [InlineData(false, false, "linux-musl-arm64", Architecture.Arm64, "linux-musl-arm64")]
    [InlineData(false, false, "linux-x64", Architecture.X64, "linux-x64")]
    [InlineData(true, false, "win-x64", Architecture.X64, "win-x64")]
    [InlineData(false, true, "osx-arm64", Architecture.Arm64, "osx-arm64")]
    public void DefaultRuntimeIdentifier_PreservesHostLibc(
        bool isWindows,
        bool isMacOS,
        string hostRuntimeIdentifier,
        Architecture architecture,
        string expected)
        => Assert.Equal(expected, PowerShellCompilationArtifactBuilder.GetDefaultRuntimeIdentifier(
            isWindows,
            isMacOS,
            hostRuntimeIdentifier,
            architecture));

    [Fact]
    public void DefaultRuntimeIdentifier_RequiresExplicitLinuxRidWhenLibcIsUnknown()
        => Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationArtifactBuilder.GetDefaultRuntimeIdentifier(
                isWindows: false,
                isMacOS: false,
                hostRuntimeIdentifier: null,
                Architecture.X64));
}
