using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_TypedLibraryProducesRunnableClrMethodAndHonestManifest()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);
        Assert.Equal(64, result.Manifest.ArtifactSha256.Length);

        using var assemblyStream = File.OpenRead(result.ArtifactPath);
        var loadContext = new AssemblyLoadContext("PowerForgeCompilationProof", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(assemblyStream);
            var method = assembly.GetType("PowerForge.Compiled.PowerForge_TypedProofMethods", throwOnError: true)!
                .GetMethod("Get_AllowedAverageMs", BindingFlags.Public | BindingFlags.Static)!;
            Assert.Equal(130d, method.Invoke(null, new object[] { 100d, 0.2d, 30d }));
            Assert.Equal(150d, method.Invoke(null, new object[] { 100d, 0.5d, 30d }));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void Build_PackagedExecutableRunsEmbeddedScriptAndDeclaresRuntimeFallback()
    {
        using var fixture = ArtifactFixture.Create(
            """
            param([string] $Name)
            if ($Name -eq 'Fail') {
                Write-Error 'Requested failure'
                exit 7
            }
            "Hello, $Name"
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackageProof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.NotNull(result.Manifest);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(0, result.Manifest.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);
        Assert.True(result.Manifest.SingleFile);

        var startInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--Name");
        startInfo.ArgumentList.Add("Ada");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Packaged executable did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Contains("Hello, Ada", standardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);

        var failureStartInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        failureStartInfo.ArgumentList.Add("--Name=Fail");
        using var failureProcess = Process.Start(failureStartInfo)!;
        var failureOutput = failureProcess.StandardOutput.ReadToEnd();
        var failureError = failureProcess.StandardError.ReadToEnd();
        Assert.True(failureProcess.WaitForExit(60_000), "Packaged executable failure case did not exit within 60 seconds.");
        Assert.True(failureProcess.ExitCode == 7, $"Exit code: {failureProcess.ExitCode}{Environment.NewLine}{failureError}{Environment.NewLine}{failureOutput}");
        Assert.Contains("Requested failure", failureError, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(failureOutput), failureOutput);
    }

    [Fact]
    public void Build_BinaryModuleImportsAsTypedCmdletWithoutScriptFallback()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }
            function Get-Values {
                param([int[]] $Values)
                return $Values
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BinaryProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.NotNull(result.Manifest);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(2, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);

        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module -Name '{escapedModulePath}' -Force; Get-AllowedAverageMs 100 0.5 30; $values = @(Get-Values -Values @(1, 2, 3)); \"$($values.Count):$($values -join ',')\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Binary module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal(new[] { "150", "3:1,2,3" }, standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_HybridModuleImportsTypedCmdletAndUnsupportedScriptFallbackTogether()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }

            function Get-Greeting {
                param([string] $Name)
                Write-Output "Hello, $Name"
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.EndsWith(".psm1", result.ArtifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Manifest);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);
        Assert.Equal(2, result.Manifest.Files.Length);
        Assert.All(result.Manifest.Files, file => Assert.True(File.Exists(file.Path), file.Path));
        Assert.Contains(result.Manifest.Files, file => file.Role == "TypedAssembly" && file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module -Name '{escapedModulePath}' -Force; Get-AllowedAverageMs 100 0.5 30; Get-Greeting Ada";
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Hybrid module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal(new[] { "150", "Hello, Ada" }, standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_HybridClrLibraryReportsUnsupportedUnitsAsOmittedNotRuntimeFallback()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-TypedValue {
                param([double] $Value)
                return $Value
            }
            function Get-DynamicValue {
                param([string] $Path)
                return Get-Item -LiteralPath $Path
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridLibraryProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Hybrid);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(1, result.Manifest.OmittedUnits);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation);
    }

    [Fact]
    public void Build_Net472BinaryModuleImportsInWindowsPowerShell51()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            """
            function Get-TriangularNumber {
                param([int] $Count)
                [long] $total = 0
                for ([int] $i = 1; $i -le $Count; $i++) { $total += $i }
                return $total
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DesktopProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net472"
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Import-Module -Name '{escapedModulePath}' -Force; Get-TriangularNumber 1000");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Windows PowerShell 5.1 module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal("500500", standardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    private sealed class ArtifactFixture : IDisposable
    {
        private ArtifactFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string OutputPath { get; }

        public static ArtifactFixture Create(string source)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(rootPath, "output");
            Directory.CreateDirectory(outputPath);
            var scriptPath = Path.Combine(rootPath, "input.ps1");
            File.WriteAllText(scriptPath, source);
            return new ArtifactFixture(rootPath, scriptPath, outputPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}
