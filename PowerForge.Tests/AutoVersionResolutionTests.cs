using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class AutoVersionResolutionTests
{
    [Fact]
    public void Plan_ResolvesAutoVersionFromLocalPsd1()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "3.2.1");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "auto",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal("3.2.1", plan.ExpectedVersion);
            Assert.Equal("3.2.1", plan.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildToStaging_ResolvesAutoVersionFromSourceManifest()
    {
        var source = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? stagingPath = null;
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(source.FullName, moduleName, "1.2.3");

            var pipeline = new ModuleBuildPipeline(new NullLogger());
            var spec = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = source.FullName,
                Version = "auto",
                CsprojPath = null,
                ExcludeDirectories = Array.Empty<string>(),
                ExcludeFiles = Array.Empty<string>()
            };

            var res = pipeline.BuildToStaging(spec);
            stagingPath = res.StagingPath;

            Assert.True(ManifestEditor.TryGetTopLevelString(res.ManifestPath, "ModuleVersion", out var version));
            Assert.Equal("1.2.3", version);
        }
        finally
        {
            try { if (!string.IsNullOrWhiteSpace(stagingPath)) Directory.Delete(stagingPath!, recursive: true); } catch { }
            try { source.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void InstallFromStaging_ResolvesAutoVersionFromStagingManifest()
    {
        var staging = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var installRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(staging.FullName, moduleName, "4.5.6");

            var pipeline = new ModuleBuildPipeline(new NullLogger());
            var spec = new ModuleInstallSpec
            {
                Name = moduleName,
                Version = "auto",
                StagingPath = staging.FullName,
                Strategy = InstallationStrategy.Exact,
                KeepVersions = 1,
                Roots = new[] { installRoot.FullName }
            };

            var res = pipeline.InstallFromStaging(spec, updateManifestToResolvedVersion: false);

            Assert.Equal("4.5.6", res.Version);
            Assert.True(Directory.Exists(Path.Combine(installRoot.FullName, moduleName, "4.5.6")));
        }
        finally
        {
            try { staging.Delete(recursive: true); } catch { }
            try { installRoot.Delete(recursive: true); } catch { }
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

