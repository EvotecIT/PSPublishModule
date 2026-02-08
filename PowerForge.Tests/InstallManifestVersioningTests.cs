using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class InstallManifestVersioningTests
{
    [Fact]
    public void InstallFromStaging_DoesNotPatchModuleVersion_WhenUpdateManifestToResolvedVersionIsFalse()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "Foo";
            var staging = Path.Combine(temp.FullName, "staging");
            var roots = Path.Combine(temp.FullName, "roots");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(roots);

            WriteMinimalModule(staging, moduleName, "3.0.0");

            // Force AutoRevision to choose 3.0.0.1 by creating an existing 3.0.0 folder.
            var existing = Path.Combine(roots, moduleName, "3.0.0");
            Directory.CreateDirectory(existing);

            var pipeline = new ModuleBuildPipeline(new NullLogger());
            var spec = new ModuleInstallSpec
            {
                Name = moduleName,
                Version = "3.0.0",
                StagingPath = staging,
                Strategy = InstallationStrategy.AutoRevision,
                KeepVersions = 10,
                Roots = new[] { roots },
                UpdateManifestToResolvedVersion = false
            };

            var result = pipeline.InstallFromStaging(spec);

            Assert.Equal("3.0.0.1", result.Version);
            Assert.Contains(Path.Combine(roots, moduleName, "3.0.0.1"), result.InstalledPaths, StringComparer.OrdinalIgnoreCase);

            var installedPsd1 = Path.Combine(roots, moduleName, "3.0.0.1", $"{moduleName}.psd1");
            Assert.True(File.Exists(installedPsd1));
            Assert.True(ManifestEditor.TryGetTopLevelString(installedPsd1, "ModuleVersion", out var installedVersion));
            Assert.Equal("3.0.0", installedVersion);
        }
        finally
        {
            try { temp.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void InstallFromStaging_PatchesModuleVersion_WhenUpdateManifestToResolvedVersionIsTrue()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "Foo";
            var staging = Path.Combine(temp.FullName, "staging");
            var roots = Path.Combine(temp.FullName, "roots");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(roots);

            WriteMinimalModule(staging, moduleName, "3.0.0");

            var existing = Path.Combine(roots, moduleName, "3.0.0");
            Directory.CreateDirectory(existing);

            var pipeline = new ModuleBuildPipeline(new NullLogger());
            var spec = new ModuleInstallSpec
            {
                Name = moduleName,
                Version = "3.0.0",
                StagingPath = staging,
                Strategy = InstallationStrategy.AutoRevision,
                KeepVersions = 10,
                Roots = new[] { roots },
                UpdateManifestToResolvedVersion = true
            };

            var result = pipeline.InstallFromStaging(spec);

            Assert.Equal("3.0.0.1", result.Version);

            var installedPsd1 = Path.Combine(roots, moduleName, "3.0.0.1", $"{moduleName}.psd1");
            Assert.True(File.Exists(installedPsd1));
            Assert.True(ManifestEditor.TryGetTopLevelString(installedPsd1, "ModuleVersion", out var installedVersion));
            Assert.Equal("3.0.0.1", installedVersion);
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
