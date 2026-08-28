using System;
using System.Diagnostics;
using System.IO;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictTypedExecutableUsesClrDefaultForOmittedOptionalParameter()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Count); return $Count");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableOptionalParameter",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var processResult = RunProcess(result.ArtifactPath!);

        Assert.Equal(0, processResult.ExitCode);
        Assert.Equal("0", processResult.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(processResult.StandardError), processResult.StandardError);
    }

    [Fact]
    public void Build_StrictTypedExecutableRejectsEmptyMandatoryString()
    {
        using var fixture = ArtifactFixture.Create("param([Parameter(Mandatory)] [string] $Name); return $Name");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableMandatoryString",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var processResult = RunProcess(result.ArtifactPath!, "--Name=");

        Assert.Equal(1, processResult.ExitCode);
        Assert.Contains("cannot be an empty string", processResult.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(processResult.StandardOutput), processResult.StandardOutput);
    }

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.NotNull(result.Manifest.SemanticProfile);
        Assert.NotNull(result.Manifest.PublicAbi);
        Assert.Equal("CompiledPowerShellScript", result.Manifest.PublicAbi.TypeName);
        Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
        Assert.True(result.Manifest.DependencyClosureVerified);
        Assert.NotNull(result.Manifest.DependencyClosure);
        Assert.StartsWith("DotNetSingleFile/", result.Manifest.DependencyClosure.ArtifactFormat, StringComparison.Ordinal);
        Assert.True(result.Manifest.DependencyClosure.BundledEntries > 0);
        Assert.Empty(result.Manifest.DependencyClosure.Limitations);
        Assert.Equal(64, result.Manifest.GeneratedSourceSha256.Length);
        Assert.True(result.Manifest.CompiledMethods == 1,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

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
    public void Build_StrictTypedExecutableTreatsNegativeNumericTokenAsPositionalValue()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableNegativePositional",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var processResult = RunProcess(result.ArtifactPath!, "-3");

        Assert.Equal(0, processResult.ExitCode);
        Assert.Equal("-3", processResult.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(processResult.StandardError), processResult.StandardError);
    }

    [Fact]
    public void Build_StrictTypedExecutableUsesCurrentCultureForOutput()
    {
        using var fixture = ArtifactFixture.Create("return 1.5");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableCulture",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var processResult = RunProcess(result.ArtifactPath!);

        Assert.Equal(0, processResult.ExitCode);
        Assert.Equal(1.5d.ToString(System.Globalization.CultureInfo.CurrentCulture), processResult.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(processResult.StandardError), processResult.StandardError);
    }

    [Theory]
    [InlineData("return [System.TimeSpan]::new(0, 0, 1)")]
    [InlineData("return [System.Version]::new(1, 2)")]
    [InlineData("return ''.GetType()")]
    public void Build_StrictTypedExecutableRejectsStructuredOutputThatRequiresPowerShellFormatting(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableStructuredOutput",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("PowerShell formatting semantics", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            Optimization = PowerShellCompilationExecutableOptimization.Trimmed
        };

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains("SelfContained", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridExecutableRegistersTypedCmdletsAndRetainsScriptFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int] $Value); function Get-Double { param([int] $Number) [int] $Result = $Number; $Result += $Number; return $Result }; Get-Double -Number $Value");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.True(result.Manifest.ContainsEmbeddedPowerShellSource);
        Assert.True(result.Manifest.CompiledMethods == 1,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.True(result.Manifest.RuntimeFallbackUnits > 0);
        Assert.Equal(1, result.Manifest.Boundaries!.TypedEntryPoints);
        var run = RunProcess(result.ArtifactPath!, "-Value", "21");
        Assert.Equal((0, "42", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridExecutableHashesAndExecutesRewrittenCompiledDependencies()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Helper.ps1\"; 'ready'");
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(
            helper,
            "function Get-Triple { param([int] $Number) [int] $Result = $Number; $Result += $Number; $Result += $Number; return $Result }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridDependencyExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var run = RunProcess(result.ArtifactPath!);
        Assert.Equal((0, "ready", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot
        };

        Assert.False(PowerShellCompilationArtifactBuilder.ShouldEnablePublishSingleFile(spec));
    }

    [Fact]
    public void Build_StrictArtifactDoesNotPublishWhenDeliveredClosureCannotBeCertified()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var builder = new PowerShellCompilationArtifactBuilder(_ => new PowerShellCompilationDependencyClosure
        {
            Verified = false,
            Limitations = { "Fixture executable format is opaque." }
        });

        var result = builder.Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.UncertifiedStrictArtifact",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactPath);
        Assert.Null(result.ManifestPath);
        Assert.Null(result.Manifest);
        Assert.Contains("fully certified delivered dependency closure", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opaque", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            SignArtifact = true,
            CertificateThumbprint = new string('0', 40)
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("code-signing certificate", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Typed executable did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput, standardError);
    }
}
