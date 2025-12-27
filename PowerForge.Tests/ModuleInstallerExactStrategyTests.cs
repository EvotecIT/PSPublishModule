using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleInstallerExactStrategyTests
{
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
