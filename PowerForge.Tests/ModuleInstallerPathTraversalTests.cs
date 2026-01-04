using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleInstallerPathTraversalTests
{
    [Theory]
    [InlineData(@"..\evil")]
    [InlineData(@"..")]
    [InlineData(@".")]
    [InlineData(@"a\b")]
    [InlineData(@"a/b")]
    [InlineData(@"C:\Windows")]
    public void InstallFromStaging_RejectsUnsafeModuleName(string moduleName)
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging"));
            File.WriteAllText(Path.Combine(staging.FullName, "Test.psm1"), "# test");

            var installer = new ModuleInstaller(new NullLogger());
            var options = new ModuleInstallerOptions(new[] { tempRoot.FullName }, InstallationStrategy.Exact, keepVersions: 1);

            Assert.ThrowsAny<ArgumentException>(() => installer.InstallFromStaging(staging.FullName, moduleName, "1.0.0", options));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(@"..\evil")]
    [InlineData(@"..")]
    [InlineData(@".")]
    [InlineData(@"1.0.0\..\evil")]
    [InlineData(@"1.0.0/../evil")]
    [InlineData(@"C:\Windows")]
    public void InstallFromStaging_RejectsUnsafeModuleVersion(string moduleVersion)
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging"));
            File.WriteAllText(Path.Combine(staging.FullName, "Test.psm1"), "# test");

            var installer = new ModuleInstaller(new NullLogger());
            var options = new ModuleInstallerOptions(new[] { tempRoot.FullName }, InstallationStrategy.Exact, keepVersions: 1);

            Assert.ThrowsAny<ArgumentException>(() => installer.InstallFromStaging(staging.FullName, "TestModule", moduleVersion, options));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

