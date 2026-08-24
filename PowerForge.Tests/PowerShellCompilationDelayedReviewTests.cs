using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
