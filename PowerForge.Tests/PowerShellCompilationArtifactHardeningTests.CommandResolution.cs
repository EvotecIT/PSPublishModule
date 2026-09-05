using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridPreservesRequiredModuleCommandShadowAcrossExpressionShapes(
        string targetFramework,
        string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-DirectShadow { [CmdletBinding()] param() return Get-Date }; " +
            "function Get-UntypedShadow { [CmdletBinding()] param() $value = Get-Date; return $value.Year }; " +
            "function Get-TypedShadow { [CmdletBinding()] param() [datetime] $value = Get-Date; return $value.Year }; " +
            "Export-ModuleMember -Function Get-DirectShadow,Get-UntypedShadow,Get-TypedShadow",
            ".psm1");
        var manifestPath = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(
            manifestPath,
            "@{ RootModule='input.psm1'; ModuleVersion='1.0.0'; RequiredModules=@('GenericShadowDependency'); " +
            "FunctionsToExport=@('Get-DirectShadow','Get-UntypedShadow','Get-TypedShadow'); CmdletsToExport=@() }");
        var dependencyRoot = Path.Combine(fixture.RootPath, "modules", "GenericShadowDependency");
        Directory.CreateDirectory(dependencyRoot);
        File.WriteAllText(
            Path.Combine(dependencyRoot, "GenericShadowDependency.psm1"),
            "function Get-Date { [datetime] '2001-02-03T04:05:06' }; Export-ModuleMember -Function Get-Date");
        File.WriteAllText(
            Path.Combine(dependencyRoot, "GenericShadowDependency.psd1"),
            "@{ RootModule='GenericShadowDependency.psm1'; ModuleVersion='1.0.0'; FunctionsToExport=@('Get-Date') }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RequiredModuleCommandShadow" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifestPath,
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.DoesNotContain(
            result.Manifest!.CommandProviders,
            static provider => provider.ProviderId == "powerforge.command.runtime-state.get-date");
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(3, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(2, result.Manifest.CompiledMethods);
        var command = "@((Get-DirectShadow).Year,(Get-UntypedShadow),(Get-TypedShadow)) -join '|'";
        var modulePath = Path.Combine(fixture.RootPath, "modules");
        var original = RunModuleWithPath(host, manifestPath, command, modulePath);
        var compiled = RunModuleWithPath(host, result.ArtifactPath!, command, modulePath);

        Assert.Equal((0, "2001|2001|2001"), (original.ExitCode, original.StandardOutput.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Fact]
    public void Build_UnqualifiedHostCommandFallsBackInHybridAndFailsClosedInStrict()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UnqualifiedDate { [CmdletBinding()] param() return Get-Date }; Export-ModuleMember -Function @('Get-UnqualifiedDate')",
            ".psm1");

        var hybrid = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, "hybrid"),
            "PowerForge.UnqualifiedCommandHybrid",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));
        var strict = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, "strict"),
            "PowerForge.UnqualifiedCommandStrict",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(hybrid.Succeeded, hybrid.Error + Environment.NewLine + hybrid.BuildOutput);
        Assert.Equal(0, hybrid.Manifest!.CompiledMethods);
        Assert.Equal(1, hybrid.Manifest.RuntimeFallbackUnits);
        Assert.False(strict.Succeeded);
        Assert.Null(strict.ArtifactPath);
        Assert.Contains("runtime", strict.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HybridPreservesCallerSessionCommandShadow()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SessionShadow { [CmdletBinding()] param() $value = Get-Date; return $value.Year }; " +
            "Export-ModuleMember -Function @('Get-SessionShadow')",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SessionCommandShadow",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.DoesNotContain(
            result.Manifest.CommandProviders,
            static provider => provider.ProviderId == "powerforge.command.runtime-state.get-date");
        const string command = "function global:Get-Date { [datetime] '2002-03-04T05:06:07' }; Get-SessionShadow";
        var original = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");

        Assert.Equal((0, "2002", string.Empty), (original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunModuleWithPath(
        string host,
        string modulePath,
        string command,
        string additionalModulePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["PSModulePath"] = additionalModulePath + Path.PathSeparator +
            (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty);
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Import-Module -Name '{modulePath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Command-shadow module proof timed out.");
        return (process.ExitCode, output, error);
    }
}
