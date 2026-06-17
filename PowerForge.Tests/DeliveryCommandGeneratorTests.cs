using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DeliveryCommandGeneratorTests
{
    [Fact]
    public void Generate_CreatesInstallAndUpdateScripts()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                IncludePaths = new[] { "Config/**", "Scripts/*.ps1" },
                ExcludePaths = new[] { "Config/local/**" },
                PreservePaths = new[] { "Config/**", "Docs" },
                OverwritePaths = new[] { "Artefacts/**" },
                GenerateInstallCommand = true,
                GenerateUpdateCommand = true
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            var generated = generator.Generate(root.FullName, "EFAdminManager", delivery);

            Assert.Contains(generated, g => g.Name == "Install-EFAdminManager");
            Assert.Contains(generated, g => g.Name == "Update-EFAdminManager");

            var installPath = Path.Combine(root.FullName, "Public", "Install-EFAdminManager.ps1");
            var updatePath = Path.Combine(root.FullName, "Public", "Update-EFAdminManager.ps1");

            Assert.True(File.Exists(installPath));
            Assert.True(File.Exists(updatePath));

            var installContent = File.ReadAllText(installPath);
            Assert.Contains("function Install-EFAdminManager", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-Host \"[EFAdminManager] Installing bundled package content\"", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-DeliveryError", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $IncludePaths = @('Config/**', 'Scripts/*.ps1')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $ExcludePaths = @('Config/local/**')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $PreservePaths = @('Config/**', 'Docs')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $OverwritePaths = @('Artefacts/**')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $RepositoryCredentialSecretFilePath", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $__DeliveryNoBootstrap", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Resolve-DeliverySecret", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Test-DeliveryPathMatch", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Test-DeliveryPathIncluded", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Get-DeliveryAction", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[ValidateSet('Merge', 'Refresh', 'Overwrite', 'Skip', 'Stop')]", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", installContent, StringComparison.Ordinal);

            var updateContent = File.ReadAllText(updatePath);
            Assert.Contains("function Update-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-Host \"[EFAdminManager] Updating bundled package content via Install-EFAdminManager\"", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Delivery.InstallCommandMissing", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[ValidateSet('Merge', 'Refresh', 'Overwrite', 'Skip', 'Stop')]", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $OnExists = 'Refresh'", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$forward['OnExists'] = $OnExists", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-EFAdminManager @forward", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Install-EFAdminManager @PSBoundParameters", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $IncludePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $ExcludePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $PreservePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $OverwritePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $RepositoryCredentialSecretFilePath", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $__DeliveryNoBootstrap", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", updateContent, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GeneratedUpdate_IncludeExcludePaths_ScopeManagedPackageFiles()
    {
        var powerShell = FindPowerShellExecutable();
        if (powerShell is null)
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                IncludePaths = new[] { "Config/**", "Scripts/*.ps1" },
                ExcludePaths = new[] { "Config/ignored.json", "Config/local/**" },
                PreservePaths = new[] { "Config/preserved.json" },
                GenerateInstallCommand = true,
                GenerateUpdateCommand = true
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            generator.Generate(root.FullName, "TestDelivery", delivery);

            var config = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals", "Config"));
            var local = Directory.CreateDirectory(Path.Combine(config.FullName, "local"));
            var scripts = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals", "Scripts"));
            var docs = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals", "Docs"));
            File.WriteAllText(Path.Combine(config.FullName, "config.sample.json"), "package-new");
            File.WriteAllText(Path.Combine(config.FullName, "ignored.json"), "package-ignored-new");
            File.WriteAllText(Path.Combine(config.FullName, "preserved.json"), "package-preserved-new");
            File.WriteAllText(Path.Combine(local.FullName, "default.json"), "package-local-new");
            File.WriteAllText(Path.Combine(scripts.FullName, "tool.ps1"), "package-script-new");
            File.WriteAllText(Path.Combine(docs.FullName, "readme.md"), "package-doc-new");
            File.WriteAllText(Path.Combine(root.FullName, "TestDelivery.psm1"), """
                . "$PSScriptRoot\Public\Install-TestDelivery.ps1"
                . "$PSScriptRoot\Public\Update-TestDelivery.ps1"
                """);

            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "Destination"));
            var destinationConfig = Directory.CreateDirectory(Path.Combine(destination.FullName, "Config"));
            var destinationLocal = Directory.CreateDirectory(Path.Combine(destinationConfig.FullName, "local"));
            var destinationScripts = Directory.CreateDirectory(Path.Combine(destination.FullName, "Scripts"));
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "config.sample.json"), "package-old");
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "ignored.json"), "local-ignored");
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "preserved.json"), "local-preserved");
            File.WriteAllText(Path.Combine(destinationLocal.FullName, "default.json"), "local-default");
            File.WriteAllText(Path.Combine(destinationScripts.FullName, "tool.ps1"), "script-old");

            var scriptPath = Path.Combine(root.FullName, "run-update-scoped.ps1");
            File.WriteAllText(scriptPath, $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module -Name '{{EscapePowerShellString(Path.Combine(root.FullName, "TestDelivery.psm1"))}}' -Force
                Update-TestDelivery -Path '{{EscapePowerShellString(destination.FullName)}}' | Out-Null
                """);

            var result = RunPowerShell(powerShell, scriptPath);

            Assert.True(result.ExitCode == 0, $"PowerShell failed with exit code {result.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
            Assert.Equal("package-new", File.ReadAllText(Path.Combine(destinationConfig.FullName, "config.sample.json")));
            Assert.Equal("local-ignored", File.ReadAllText(Path.Combine(destinationConfig.FullName, "ignored.json")));
            Assert.Equal("local-preserved", File.ReadAllText(Path.Combine(destinationConfig.FullName, "preserved.json")));
            Assert.Equal("local-default", File.ReadAllText(Path.Combine(destinationLocal.FullName, "default.json")));
            Assert.Equal("package-script-new", File.ReadAllText(Path.Combine(destinationScripts.FullName, "tool.ps1")));
            Assert.False(File.Exists(Path.Combine(destination.FullName, "Docs", "readme.md")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GeneratedUpdate_DefaultRefresh_OverwritesPackageFiles_AndPreservesLocalExtras()
    {
        var powerShell = FindPowerShellExecutable();
        if (powerShell is null)
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                PreservePaths = new[] { "Config/preserved.json" },
                GenerateInstallCommand = true,
                GenerateUpdateCommand = true
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            generator.Generate(root.FullName, "TestDelivery", delivery);

            var internals = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals", "Config"));
            File.WriteAllText(Path.Combine(internals.FullName, "config.sample.json"), "package-new");
            File.WriteAllText(Path.Combine(internals.FullName, "preserved.json"), "package-new-preserved");
            File.WriteAllText(Path.Combine(root.FullName, "TestDelivery.psm1"), """
                . "$PSScriptRoot\Public\Install-TestDelivery.ps1"
                . "$PSScriptRoot\Public\Update-TestDelivery.ps1"
                """);

            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "Destination"));
            var destinationConfig = Directory.CreateDirectory(Path.Combine(destination.FullName, "Config"));
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "config.sample.json"), "package-old");
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "preserved.json"), "local-preserved");
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "config.json"), "local-config");
            File.WriteAllText(Path.Combine(destination.FullName, "local-only.txt"), "local-extra");

            var scriptPath = Path.Combine(root.FullName, "run-update.ps1");
            File.WriteAllText(scriptPath, $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module -Name '{{EscapePowerShellString(Path.Combine(root.FullName, "TestDelivery.psm1"))}}' -Force
                Update-TestDelivery -Path '{{EscapePowerShellString(destination.FullName)}}' | Out-Null
                """);

            var result = RunPowerShell(powerShell, scriptPath);

            Assert.True(result.ExitCode == 0, $"PowerShell failed with exit code {result.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
            Assert.Equal("package-new", File.ReadAllText(Path.Combine(destinationConfig.FullName, "config.sample.json")));
            Assert.Equal("local-preserved", File.ReadAllText(Path.Combine(destinationConfig.FullName, "preserved.json")));
            Assert.Equal("local-config", File.ReadAllText(Path.Combine(destinationConfig.FullName, "config.json")));
            Assert.Equal("local-extra", File.ReadAllText(Path.Combine(destination.FullName, "local-only.txt")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GeneratedInstall_Overwrite_RemainsDestructive()
    {
        var powerShell = FindPowerShellExecutable();
        if (powerShell is null)
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                GenerateInstallCommand = true
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            generator.Generate(root.FullName, "TestDelivery", delivery);

            var internals = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals"));
            File.WriteAllText(Path.Combine(internals.FullName, "package.txt"), "package-new");
            File.WriteAllText(Path.Combine(root.FullName, "TestDelivery.psm1"), """
                . "$PSScriptRoot\Public\Install-TestDelivery.ps1"
                """);

            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "Destination"));
            File.WriteAllText(Path.Combine(destination.FullName, "local-only.txt"), "local-extra");

            var scriptPath = Path.Combine(root.FullName, "run-install.ps1");
            File.WriteAllText(scriptPath, $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module -Name '{{EscapePowerShellString(Path.Combine(root.FullName, "TestDelivery.psm1"))}}' -Force
                Install-TestDelivery -Path '{{EscapePowerShellString(destination.FullName)}}' -OnExists Overwrite | Out-Null
                """);

            var result = RunPowerShell(powerShell, scriptPath);

            Assert.True(result.ExitCode == 0, $"PowerShell failed with exit code {result.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
            Assert.Equal("package-new", File.ReadAllText(Path.Combine(destination.FullName, "package.txt")));
            Assert.False(File.Exists(Path.Combine(destination.FullName, "local-only.txt")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Generate_Skips_WhenScriptAlreadyExists()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var publicFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Public"));
            File.WriteAllText(Path.Combine(publicFolder.FullName, "Install-EFAdminManager.ps1"), "function Install-EFAdminManager { }");

            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                GenerateInstallCommand = true,
                GenerateUpdateCommand = false
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            var generated = generator.Generate(root.FullName, "EFAdminManager", delivery);

            Assert.DoesNotContain(generated, g => g.Name == "Install-EFAdminManager");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? FindPowerShellExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "pwsh", "powershell.exe" }
            : new[] { "pwsh" };

        return candidates.FirstOrDefault(CommandExists);
    }

    private static bool CommandExists(string command)
    {
        try
        {
            var probe = new ProcessStartInfo
            {
                FileName = command,
                ArgumentList = { "-NoLogo", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(probe);
            if (process is null)
                return false;

            process.WaitForExit(10000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShell(string executable, string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            ArgumentList = { "-NoLogo", "-NoProfile", "-File", scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);

        return process.HasExited
            ? (process.ExitCode, stdout, stderr)
            : throw new TimeoutException($"{executable} did not finish. STDOUT: {stdout} STDERR: {stderr}");
    }

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
