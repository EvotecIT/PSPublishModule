using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
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
        Assert.Equal(new FileInfo(result.ArtifactPath!).Length, result.Manifest.ArtifactSizeBytes);
        Assert.All(result.Manifest.Files, file => Assert.Equal(new FileInfo(file.Path).Length, file.SizeBytes));
        Assert.Equal(64, result.Manifest.ArtifactSha256.Length);
        Assert.Contains(result.Manifest.Files, file => file.Role == "DebugSymbols" && file.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));

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
            [CmdletBinding()]
            param([Alias('f')] [switch] $Force, [string] $Name)
            if ($Name -eq 'Fail') {
                Write-Error 'Requested failure'
                exit 7
            }
            Write-Verbose 'verbose-record'
            Write-Debug 'debug-record'
            "Hello, $Name; Force=$($Force.IsPresent)"
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
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("Ada");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Packaged executable did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Contains("Hello, Ada; Force=True", standardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);

        var commonSwitchStartInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        commonSwitchStartInfo.ArgumentList.Add("--Verbose");
        commonSwitchStartInfo.ArgumentList.Add("--Debug");
        commonSwitchStartInfo.ArgumentList.Add("Grace");
        using var commonSwitchProcess = Process.Start(commonSwitchStartInfo)!;
        var commonSwitchOutput = commonSwitchProcess.StandardOutput.ReadToEnd();
        var commonSwitchError = commonSwitchProcess.StandardError.ReadToEnd();
        Assert.True(commonSwitchProcess.WaitForExit(60_000), "Packaged executable common-switch case did not exit within 60 seconds.");
        Assert.True(commonSwitchProcess.ExitCode == 0, $"Exit code: {commonSwitchProcess.ExitCode}{Environment.NewLine}{commonSwitchError}{Environment.NewLine}{commonSwitchOutput}");
        Assert.Contains("Hello, Grace; Force=False", commonSwitchOutput, StringComparison.Ordinal);
        Assert.Contains("VERBOSE: verbose-record", commonSwitchOutput, StringComparison.Ordinal);
        Assert.Contains("DEBUG: debug-record", commonSwitchOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(commonSwitchError), commonSwitchError);

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
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Strict));
        Assert.True(
            plan.CanProceed,
            string.Join(Environment.NewLine, plan.Files.SelectMany(file => file.Diagnostics.Concat(file.Units.SelectMany(unit => unit.Diagnostics))).Select(diagnostic => diagnostic.Message)));

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
            using namespace System
            param([string] $Prefix = 'Hello')

            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }

            function Get-Greeting {
                param([string] $Name)
                Write-Output "$Prefix, $Name"
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
        Assert.Equal(2, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(0, result.Manifest.OmittedUnits);
        Assert.True(result.Manifest.Files.Length >= 2);
        Assert.All(result.Manifest.Files, file => Assert.True(File.Exists(file.Path), file.Path));
        Assert.Contains(result.Manifest.Files, file => file.Role == "TypedAssembly" && file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Manifest.Files, file => file.Role == "DebugSymbols" && file.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));

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
    public void Build_RejectsAnalyzeModeBecauseItCannotProduceAnArtifact()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AnalyzeOnly",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Analyze);

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains("does not produce artifacts", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModulePreservesLiteralSelectiveExports()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-PublicValue { return 1 }
            function Get-PrivateValue { return 2 }
            Export-ModuleMember -Function @('Get-PublicValue')
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SelectiveExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module -Name '{escapedModulePath}' -Force; [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-PublicValue";
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
        Assert.True(process.WaitForExit(60_000), "Selective-export module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal(new[] { "True", "False", "1" }, standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_StrictBinaryModuleRewritesSiblingManifestWithoutBroadeningPublicSurface()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-PublicValue { return 1 }
            function Get-PrivateValue { return 2 }
            """,
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            """
            @{
                RootModule = 'input.psm1'
                ModuleVersion = '1.0.0'
                GUID = '936a8f5f-156a-470b-ad57-262cafb46748'
                FunctionsToExport = @('Get-PublicValue')
                CmdletsToExport = '*'
                VariablesToExport = @()
                AliasesToExport = @()
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ManifestExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.ArtifactPath);
        Assert.EndsWith(".psd1", result.ArtifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Manifest);
        Assert.Contains(result.Manifest!.Files, file => file.Role == "PrimaryModuleManifest" && file.Path == result.ArtifactPath);
        Assert.Contains(result.Manifest.Files, file => file.Role == "TypedAssembly" && file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var manifestText = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("PowerForge.ManifestExport.dll", manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("input.psm1", manifestText, StringComparison.OrdinalIgnoreCase);

        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module -Name '{escapedModulePath}' -Force; [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-PublicValue";
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
        Assert.True(process.WaitForExit(60_000), "Manifest-backed strict module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal(new[] { "True", "False", "1" }, standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesManifestExportsAcrossTypedAndFallbackCommands()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-CompiledValue { return 1 }
            function Get-FallbackValue { return (Get-Date).Year }
            function Get-PrivateValue { return 2 }
            """,
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            """
            @{
                RootModule = 'input.psm1'
                ModuleVersion = '1.0.0'
                GUID = 'e3013745-2f57-470e-8317-09532eb16c29'
                FunctionsToExport = @('Get-CompiledValue', 'Get-FallbackValue')
                CmdletsToExport = @()
                VariablesToExport = @()
                AliasesToExport = @()
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridManifest",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedModulePath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module -Name '{escapedModulePath}' -Force; [bool](Get-Command Get-CompiledValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-FallbackValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-CompiledValue; [int](Get-FallbackValue) -gt 2000";
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
        Assert.True(process.WaitForExit(60_000), "Manifest-backed hybrid module proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Equal(new[] { "True", "True", "False", "1", "True" }, standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void Build_RejectsDynamicManifestExportsWithoutPublishingPartialArtifacts()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; FunctionsToExport = @(Get-DynamicExport); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DynamicManifest",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("literal string", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_RejectsDynamicExportModuleMemberWithoutPublishingPartialArtifacts()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-PublicValue { return 1 }
            $exports = 'Get-PublicValue'
            Export-ModuleMember -Function $exports
            """,
            ".psm1");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DynamicExportCommand",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("non-literal export", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsCaughtExitInstrumentation()
    {
        using var fixture = ArtifactFixture.Create(
            """
            try { exit 7 }
            catch { 'caught' }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CaughtExit",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("catch behavior", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_MultiFileExecutablePreservesNestedPowerShellRuntimeTree()
    {
        using var fixture = ArtifactFixture.Create(
            """
            Import-Module Microsoft.PowerShell.Utility
            (Get-Command ConvertTo-Json).Name
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MultiFileProof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            SingleFile = false
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.SingleFile);
        Assert.True(result.Manifest.Files.Length > 1);
        var generatedAssembly = Assert.Single(result.Manifest.Files, file => file.Role == "GeneratedAssembly");
        Assert.Equal("PowerForge.MultiFileProof.dll", Path.GetFileName(generatedAssembly.Path));
        Assert.Contains(generatedAssembly.Path, PowerShellCompilationArtifactSigner.GetBuildOwnedSignableFiles(result.Manifest.Files));
        Assert.Contains(result.Manifest.Files, file => file.Path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("Modules", StringComparer.OrdinalIgnoreCase));
        Assert.All(result.Manifest.Files, file => Assert.True(File.Exists(file.Path), file.Path));

        var startInfo = new ProcessStartInfo
        {
            FileName = result.ArtifactPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Multi-file executable did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, $"Exit code: {process.ExitCode}{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.Contains("ConvertTo-Json", standardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);

        var artifactDirectory = Path.GetDirectoryName(result.ArtifactPath)!;
        var stalePath = Path.Combine(artifactDirectory, "stale.dll");
        File.WriteAllText(stalePath, "stale");

        var rebuilt = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(rebuilt.Succeeded, rebuilt.Error + Environment.NewLine + rebuilt.BuildOutput);
        Assert.False(File.Exists(stalePath));
        Assert.DoesNotContain(rebuilt.Manifest!.Files, file => Path.GetFileName(file.Path).Equals("stale.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(fixture.OutputPath, ".PowerForge.MultiFileProof.staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(fixture.OutputPath, "PowerForge.MultiFileProof.backup-*"));
    }

    [Theory]
    [InlineData("win-x64", "Proof.exe")]
    [InlineData("win-arm64", "Proof.exe")]
    [InlineData("linux-x64", "Proof")]
    [InlineData("osx-arm64", "Proof")]
    public void GetExecutableFileName_UsesTargetRidInsteadOfHost(string runtimeIdentifier, string expected)
        => Assert.Equal(expected, PowerShellCompilationArtifactBuilder.GetExecutableFileName("Proof", runtimeIdentifier));

    [Fact]
    public void Build_CrossRidExecutableUsesTargetPlatformFileName()
    {
        using var fixture = ArtifactFixture.Create("'cross-rid proof'");
        var targetRid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "linux-x64" : "win-x64";
        var expectedFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PowerForge.CrossRidProof" : "PowerForge.CrossRidProof.exe";
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CrossRidProof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            RuntimeIdentifier = targetRid
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(targetRid, result.Manifest!.RuntimeIdentifier);
        Assert.Equal(expectedFileName, Path.GetFileName(result.ArtifactPath));
        Assert.True(File.Exists(result.ArtifactPath), result.ArtifactPath);
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

        public static ArtifactFixture Create(string source, string extension = ".ps1")
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(rootPath, "output");
            Directory.CreateDirectory(outputPath);
            var scriptPath = Path.Combine(rootPath, "input" + extension);
            File.WriteAllText(scriptPath, source);
            return new ArtifactFixture(rootPath, scriptPath, outputPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}
