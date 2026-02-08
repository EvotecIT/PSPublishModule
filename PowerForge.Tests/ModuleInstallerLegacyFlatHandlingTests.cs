using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleInstallerLegacyFlatHandlingTests
{
    [Fact]
    public void InstallFromStaging_ConvertsLegacyFlatInstall_AndPinsConvertedVersionFromPrune()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Path.Combine(temp.FullName, "staging");
            var roots = Path.Combine(temp.FullName, "roots");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(roots);

            const string moduleName = "Foo";
            WriteMinimalModule(staging, moduleName, "3.0.0");

            var moduleRoot = Path.Combine(roots, moduleName);
            Directory.CreateDirectory(moduleRoot);
            WriteMinimalModule(moduleRoot, moduleName, "2.0.26");
            Directory.CreateDirectory(Path.Combine(moduleRoot, "Public"));
            File.WriteAllText(Path.Combine(moduleRoot, "Public", "Legacy.ps1"), "# legacy");

            var installer = new ModuleInstaller(new NullLogger());
            var opts = new ModuleInstallerOptions(
                destinationRoots: new[] { roots },
                strategy: InstallationStrategy.Exact,
                keepVersions: 1,
                legacyFlatHandling: LegacyFlatModuleHandling.Convert,
                preserveVersions: null);

            _ = installer.InstallFromStaging(staging, moduleName, "3.0.0", opts);

            Assert.False(File.Exists(Path.Combine(moduleRoot, $"{moduleName}.psd1")));
            Assert.True(Directory.Exists(Path.Combine(moduleRoot, "2.0.26")));
            Assert.True(File.Exists(Path.Combine(moduleRoot, "2.0.26", $"{moduleName}.psd1")));
            Assert.True(Directory.Exists(Path.Combine(moduleRoot, "3.0.0")));
            Assert.True(File.Exists(Path.Combine(moduleRoot, "3.0.0", $"{moduleName}.psd1")));
        }
        finally
        {
            try { temp.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void InstallFromStaging_PreservesPinnedVersionsDuringPrune()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var staging = Path.Combine(temp.FullName, "staging");
            var roots = Path.Combine(temp.FullName, "roots");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(roots);

            const string moduleName = "Foo";
            WriteMinimalModule(staging, moduleName, "3.0.0");

            var moduleRoot = Path.Combine(roots, moduleName);
            Directory.CreateDirectory(Path.Combine(moduleRoot, "1.0.0"));
            WriteMinimalModule(Path.Combine(moduleRoot, "1.0.0"), moduleName, "1.0.0");
            Directory.CreateDirectory(Path.Combine(moduleRoot, "2.0.0"));
            WriteMinimalModule(Path.Combine(moduleRoot, "2.0.0"), moduleName, "2.0.0");

            var installer = new ModuleInstaller(new NullLogger());
            var opts = new ModuleInstallerOptions(
                destinationRoots: new[] { roots },
                strategy: InstallationStrategy.Exact,
                keepVersions: 1,
                legacyFlatHandling: LegacyFlatModuleHandling.Ignore,
                preserveVersions: new[] { "1.0.0" });

            _ = installer.InstallFromStaging(staging, moduleName, "3.0.0", opts);

            Assert.True(Directory.Exists(Path.Combine(moduleRoot, "3.0.0")));
            Assert.True(Directory.Exists(Path.Combine(moduleRoot, "1.0.0")));
            Assert.False(Directory.Exists(Path.Combine(moduleRoot, "2.0.0")));
        }
        finally
        {
            try { temp.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }
}

