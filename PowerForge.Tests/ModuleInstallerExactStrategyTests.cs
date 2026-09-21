using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleInstallerExactStrategyTests
{
    [Fact]
    public void InstallFromStaging_Exact_RejectsInvalidPreparedCopyBeforeOverwritingOrPruning()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            File.WriteAllText(Path.Combine(staging.FullName, "TestModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(staging.FullName, "Dependency.dll"), "new dependency");
            var modules = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            var moduleRoot = Directory.CreateDirectory(Path.Combine(modules.FullName, "TestModule"));
            var existing = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "1.0.0"));
            File.WriteAllText(Path.Combine(existing.FullName, "TestModule.psd1"), "old version");
            var older = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "0.9.0"));
            File.WriteAllText(Path.Combine(older.FullName, "TestModule.psd1"), "older version");
            var options = new ModuleInstallerOptions(
                [modules.FullName], InstallationStrategy.Exact, keepVersions: 1);

            Assert.Throws<UnauthorizedAccessException>(() =>
                new ModuleInstaller(new NullLogger()).InstallFromStagingWithPreparedValidation(
                    staging.FullName,
                    "TestModule",
                    "1.0.0",
                    options,
                    prepared =>
                    {
                        File.Delete(Path.Combine(prepared, "Dependency.dll"));
                        throw new InvalidOperationException("prepared dependency missing");
                    }));

            Assert.Equal("old version", File.ReadAllText(Path.Combine(existing.FullName, "TestModule.psd1")));
            Assert.Equal("older version", File.ReadAllText(Path.Combine(older.FullName, "TestModule.psd1")));
            Assert.False(File.Exists(Path.Combine(existing.FullName, "Dependency.dll")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void InstallFromStaging_Exact_RestoresPriorVersionWhenCommittedValidationFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            File.WriteAllText(Path.Combine(staging.FullName, "TestModule.psd1"), "new version");
            var modules = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            var moduleRoot = Directory.CreateDirectory(Path.Combine(modules.FullName, "TestModule"));
            var existing = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "1.0.0"));
            File.WriteAllText(Path.Combine(existing.FullName, "TestModule.psd1"), "old version");
            var older = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "0.9.0"));
            File.WriteAllText(Path.Combine(older.FullName, "keep.txt"), "keep older version");
            var options = new ModuleInstallerOptions(
                [modules.FullName], InstallationStrategy.Exact, keepVersions: 1);

            Assert.Throws<UnauthorizedAccessException>(() =>
                new ModuleInstaller(new NullLogger()).InstallFromStagingWithPreparedValidation(
                    staging.FullName,
                    "TestModule",
                    "1.0.0",
                    options,
                    prepared => Assert.True(File.Exists(Path.Combine(prepared, "TestModule.psd1"))),
                    committed =>
                    {
                        File.WriteAllText(Path.Combine(committed, "TestModule.psd1"), "corrupt version");
                        throw new InvalidOperationException("committed validation failed");
                    }));

            Assert.Equal("old version", File.ReadAllText(Path.Combine(existing.FullName, "TestModule.psd1")));
            Assert.Equal("keep older version", File.ReadAllText(Path.Combine(older.FullName, "keep.txt")));
            Assert.Empty(Directory.EnumerateDirectories(moduleRoot.FullName, ".backup_install_*"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }


    [Fact]
    public void InstallFromStaging_Exact_PreservesInstalledVersionBesideNewerLocalRevisions()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging"));
            var destinationRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "modules"));
            var moduleRoot = Directory.CreateDirectory(Path.Combine(destinationRoot.FullName, "TestModule"));
            File.WriteAllText(Path.Combine(staging.FullName, "TestModule.psd1"), "@{ ModuleVersion = '1.0.0' }");

            foreach (var version in new[] { "1.0.0.1", "1.0.0.2", "1.0.0.3" })
            {
                var localVersion = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, version));
                File.WriteAllText(Path.Combine(localVersion.FullName, "TestModule.psd1"), $"@{{ ModuleVersion = '{version}' }}");
            }

            var installer = new ModuleInstaller(new NullLogger());
            var options = new ModuleInstallerOptions(new[] { destinationRoot.FullName }, InstallationStrategy.Exact, keepVersions: 3);
            var result = installer.InstallFromStaging(staging.FullName, "TestModule", "1.0.0", options);

            var installedPath = Path.Combine(moduleRoot.FullName, "1.0.0");
            Assert.Equal("1.0.0", result.Version);
            Assert.Contains(installedPath, result.InstalledPaths);
            Assert.True(File.Exists(Path.Combine(installedPath, "TestModule.psd1")));
            Assert.DoesNotContain(installedPath, result.PrunedPaths);
            Assert.False(Directory.Exists(Path.Combine(moduleRoot.FullName, "1.0.0.1")));
            Assert.True(Directory.Exists(Path.Combine(moduleRoot.FullName, "1.0.0.2")));
            Assert.True(Directory.Exists(Path.Combine(moduleRoot.FullName, "1.0.0.3")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void InstallFromStaging_Exact_CleansTargetDirectory()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging"));
            var destinationRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "modules"));

            File.WriteAllText(Path.Combine(staging.FullName, "Old.ps1"), "# old");
            var removedDir = Directory.CreateDirectory(Path.Combine(staging.FullName, "RemovedDir"));
            File.WriteAllText(Path.Combine(removedDir.FullName, "stale.txt"), "stale");

            var installer = new ModuleInstaller(new NullLogger());
            var options = new ModuleInstallerOptions(new[] { destinationRoot.FullName }, InstallationStrategy.Exact, keepVersions: 3);
            var result1 = installer.InstallFromStaging(staging.FullName, "TestModule", "1.0.0", options);

            var installedPath = Path.Combine(destinationRoot.FullName, "TestModule", result1.Version);
            Assert.True(File.Exists(Path.Combine(installedPath, "Old.ps1")));
            Assert.True(Directory.Exists(Path.Combine(installedPath, "RemovedDir")));

            File.Delete(Path.Combine(staging.FullName, "Old.ps1"));
            Directory.Delete(Path.Combine(staging.FullName, "RemovedDir"), recursive: true);
            File.WriteAllText(Path.Combine(staging.FullName, "New.ps1"), "# new");

            var result2 = installer.InstallFromStaging(staging.FullName, "TestModule", "1.0.0", options);
            Assert.Equal("1.0.0", result2.Version);

            Assert.False(File.Exists(Path.Combine(installedPath, "Old.ps1")));
            Assert.False(Directory.Exists(Path.Combine(installedPath, "RemovedDir")));
            Assert.True(File.Exists(Path.Combine(installedPath, "New.ps1")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
