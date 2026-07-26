using System.Diagnostics;
using PowerForge;

public sealed class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopResolverStopsAfterBootstrapAndDoesNotReenterForMissingPowerShellResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-desktop-resolver-" + Guid.NewGuid().ToString("N"));
        var libDefault = Path.Combine(root, "Lib", "Default");
        Directory.CreateDirectory(libDefault);

        try
        {
            var moduleAssembly = BuildDesktopFixture(root);
            File.Copy(
                moduleAssembly,
                Path.Combine(libDefault, "DemoModule.dll"),
                overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false);

            var proofScript = Path.Combine(root, "Validate-ResolverLifetime.ps1");
            File.WriteAllText(
                proofScript,
                """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
$module = Get-Module DemoModule
$resolverState = & $module { $PowerForgeDesktopAssemblyResolverState }
if ($null -eq $resolverState -or
    $resolverState.PSObject.Properties.Name -notcontains 'Registered' -or
    $resolverState.Registered) {
    throw 'The Desktop assembly resolver remained registered after bootstrap.'
}
try {
    Get-Item -LiteralPath (Join-Path $PSScriptRoot 'missing-file') -ErrorAction Stop
} catch [System.Management.Automation.ItemNotFoundException] {
}
'RESOLVER_BOUNDED_OK'
""");

            var result = RunProcess(
                windowsPowerShell,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 20000);

            Assert.True(
                result.ExitCode == 0,
                $"Windows PowerShell resolver proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("RESOLVER_BOUNDED_OK", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string BuildDesktopFixture(string root)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(root, "Fixture"));
        var projectPath = Path.Combine(projectRoot.FullName, "DemoModule.csproj");
        File.WriteAllText(
            projectPath,
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>DemoModule</AssemblyName>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(
            Path.Combine(projectRoot.FullName, "Initialize.cs"),
            """
namespace DemoModule
{
    public static class Initialize
    {
    }
}
""");

        var result = RunProcess(
            "dotnet",
            $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            projectRoot.FullName,
            timeoutMilliseconds: 60000);
        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed for the Desktop resolver fixture.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        var assemblyPath = Path.Combine(
            projectRoot.FullName,
            "bin",
            "Release",
            "netstandard2.0",
            "DemoModule.dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private static ProcessResult RunProcess(
        string executable,
        string arguments,
        string workingDirectory,
        int timeoutMilliseconds)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException(
                $"Process '{executable}' did not exit within {timeoutMilliseconds} ms.");
        }

        Task.WaitAll(standardOutput, standardError);
        return new ProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
