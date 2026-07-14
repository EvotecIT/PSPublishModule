using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class PowerForgeInstallerUpgradePreservationTests
{
    [Fact]
    public async Task MajorUpgrade_PreservesMutableConfiguration_WhenOptedIn()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("POWERFORGE_RUN_MSI_INSTALL_TESTS"),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var testId = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "powerforge-msi-upgrade-" + testId);
        var dataFolderName = "PowerForgeUpgradeProof-" + testId;
        var installedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            dataFolderName);
        var installedConfig = Path.Combine(installedDirectory, "settings.json");
        var upgradeCode = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var componentGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        string? v1Msi = null;
        string? v2Msi = null;

        Directory.CreateDirectory(root);
        try
        {
            v1Msi = await CompileInstallerAsync(
                root,
                dataFolderName,
                "1.0.0",
                "v1-default",
                upgradeCode,
                componentGuid);
            v2Msi = await CompileInstallerAsync(
                root,
                dataFolderName,
                "1.0.1",
                "v2-default",
                upgradeCode,
                componentGuid);

            await RunMsiExecAsync(root, "install-v1.log", "/i", v1Msi);
            Assert.Equal("v1-default", await File.ReadAllTextAsync(installedConfig));

            await File.WriteAllTextAsync(installedConfig, "user-edited-config");
            await RunMsiExecAsync(root, "upgrade-v2.log", "/i", v2Msi);

            Assert.Equal("user-edited-config", await File.ReadAllTextAsync(installedConfig));
        }
        finally
        {
            if (v2Msi is not null)
                await TryUninstallAsync(root, "uninstall-v2.log", v2Msi);
            if (v1Msi is not null)
                await TryUninstallAsync(root, "uninstall-v1.log", v1Msi);

            TryDeleteDirectory(installedDirectory);
            TryDeleteDirectory(root);
        }
    }

    private static async Task<string> CompileInstallerAsync(
        string root,
        string dataFolderName,
        string version,
        string defaultConfig,
        string upgradeCode,
        string componentGuid)
    {
        var versionRoot = Path.Combine(root, version);
        Directory.CreateDirectory(versionRoot);
        var payloadFile = Path.Combine(versionRoot, "settings.json");
        await File.WriteAllTextAsync(payloadFile, defaultConfig);

        var definition = CreateInstaller(dataFolderName, version, payloadFile, upgradeCode, componentGuid);
        var workspace = Path.Combine(versionRoot, "installer");
        var result = await new PowerForgeWixInstallerCompiler().CompileAsync(
            definition,
            new PowerForgeWixInstallerCompileRequest
            {
                WorkingDirectory = workspace,
                SourceFileName = "Product.wxs",
                ProjectFileName = "UpgradeProof.wixproj",
                Configuration = "Release",
                Timeout = TimeSpan.FromMinutes(5)
            });

        Assert.True(
            result.Succeeded,
            $"WiX compilation failed for {version}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");

        var packages = Directory
            .GetFiles(workspace, "*.msi", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Assert.Single(packages);
    }

    private static PowerForgeInstallerDefinition CreateInstaller(
        string dataFolderName,
        string version,
        string payloadFile,
        string upgradeCode,
        string componentGuid)
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "PowerForge Upgrade Preservation Proof",
                Manufacturer = "Evotec",
                Version = version,
                UpgradeCode = upgradeCode,
                Scope = PowerForgeInstallerScope.PerMachine,
                MajorUpgradeSchedule = PowerForgeInstallerMajorUpgradeSchedule.AfterInstallExecute
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "PowerForge Upgrade Preservation Proof"
        };
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "CommonAppDataFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment
                {
                    Id = "UpgradeProofDataFolder",
                    Name = dataFolderName
                }
            }
        });
        definition.Components.Add(new PowerForgeInstallerFileComponent
        {
            Id = "MutableConfiguration",
            DirectoryRefId = "UpgradeProofDataFolder",
            Guid = componentGuid,
            FileId = "MutableConfigurationFile",
            Source = payloadFile,
            Name = "settings.json",
            Permanent = false,
            NeverOverwrite = true
        });

        return definition;
    }

    private static async Task RunMsiExecAsync(
        string logDirectory,
        string logFileName,
        string operation,
        string msiPath)
    {
        var logPath = Path.Combine(logDirectory, logFileName);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(operation);
        process.StartInfo.ArgumentList.Add(msiPath);
        process.StartInfo.ArgumentList.Add("/qn");
        process.StartInfo.ArgumentList.Add("/norestart");
        process.StartInfo.ArgumentList.Add("/l*v");
        process.StartInfo.ArgumentList.Add(logPath);
        process.StartInfo.ArgumentList.Add("REBOOT=ReallySuppress");

        Assert.True(process.Start(), "Failed to start Windows Installer.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);

        Assert.True(
            process.ExitCode is 0 or 3010,
            $"Windows Installer returned {process.ExitCode}. Log: {logPath}");
    }

    private static async Task TryUninstallAsync(string root, string logName, string msiPath)
    {
        try
        {
            await RunMsiExecAsync(root, logName, "/x", msiPath);
        }
        catch
        {
            // Best-effort cleanup keeps the original assertion as the useful failure.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for installer test artifacts.
        }
    }
}
