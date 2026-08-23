using System;
using System.Diagnostics;
using System.IO;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictTypedExecutableRunsWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            """
            param([int] $Count, [int[]] $Values)
            [long] $total = 0
            for ([int] $value = 1; $value -le $Count; $value++) {
                $total += $value
            }
            foreach ($item in $Values) {
                $total += $item
            }
            return $total
            """);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableProof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);

        var startInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--Count=5");
        startInfo.ArgumentList.Add("--Values=10");
        startInfo.ArgumentList.Add("--Values=-3");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Typed executable did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal("22", standardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);

        var missingValue = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        missingValue.ArgumentList.Add("--Count");
        missingValue.ArgumentList.Add("--Values=1");
        using var missingValueProcess = Process.Start(missingValue)!;
        var missingValueOutput = missingValueProcess.StandardOutput.ReadToEnd();
        var missingValueError = missingValueProcess.StandardError.ReadToEnd();
        Assert.True(missingValueProcess.WaitForExit(60_000), "Typed executable missing-value case did not exit within 60 seconds.");
        Assert.Equal(1, missingValueProcess.ExitCode);
        Assert.Contains("requires a value", missingValueError, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(missingValueOutput), missingValueOutput);
    }

    [Fact]
    public void Build_StrictTypedExecutableBindsOnePositionalValuePerArrayParameter()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int[]] $Values, [int] $Count); [long] $total = $Count; foreach ($value in $Values) { $total += $value }; return $total");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutablePositionalArray",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var startInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("2");
        startInfo.ArgumentList.Add("5");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Typed executable positional-array case did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal("7", standardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_RejectsOptimizationOutsideSelfContainedStrictTypedExecutable()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvalidOptimization",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            Optimization = PowerShellCompilationExecutableOptimization.Trimmed
        };

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains("SelfContained", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void NativeAotUsesItsNativeSingleArtifactInsteadOfSingleFileBundling()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NativeAotSingleArtifact",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot
        };

        Assert.False(PowerShellCompilationArtifactBuilder.ShouldEnablePublishSingleFile(spec));
    }

    [Fact]
    public void Build_SigningFailureDoesNotPublishUnsignedArtifactsOrStaleHashes()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SigningFailure",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            SignArtifact = true,
            CertificateThumbprint = new string('0', 40)
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("code-signing certificate", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
