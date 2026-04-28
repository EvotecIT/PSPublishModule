using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerMsiPrepareTests
{
    [Fact]
    public void Plan_AddsMsiPrepareStep_ForInstallerTarget()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "svc.msi",
                    PrepareFromTarget = "svc",
                    Harvest = DotNetPublishMsiHarvestMode.Auto
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var msiStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiPrepare);

            var kinds = plan.Steps.Select(s => s.Kind).ToArray();
            var publishIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Publish);
            var msiIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.MsiPrepare);
            var manifestIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Manifest);

            Assert.True(publishIndex >= 0);
            Assert.True(msiIndex > publishIndex);
            Assert.True(manifestIndex > msiIndex);

            Assert.Equal("svc.msi", msiStep.InstallerId);
            Assert.Equal("svc", msiStep.TargetName);
            Assert.False(string.IsNullOrWhiteSpace(msiStep.StagingPath));
            Assert.False(string.IsNullOrWhiteSpace(msiStep.ManifestPath));
            Assert.False(string.IsNullOrWhiteSpace(msiStep.HarvestPath));
            Assert.Equal("INSTALLFOLDER", msiStep.HarvestDirectoryRefId);
            Assert.False(string.IsNullOrWhiteSpace(msiStep.HarvestComponentGroupId));
            Assert.DoesNotContain(".", msiStep.HarvestComponentGroupId!, StringComparison.Ordinal);
            Assert.DoesNotContain("-", msiStep.HarvestComponentGroupId!, StringComparison.Ordinal);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(msiStep.StagingPath!), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(msiStep.ManifestPath!), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(msiStep.HarvestPath!), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_DeniesMsiPrepareStagingPathOutsideProjectRoot_ByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "svc.msi",
                    PrepareFromTarget = "svc",
                    StagingPath = "..\\outside\\payload"
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec, null));
            Assert.Contains("outside ProjectRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_DeniesMsiPrepareHarvestPathOutsideProjectRoot_ByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "svc.msi",
                    PrepareFromTarget = "svc",
                    Harvest = DotNetPublishMsiHarvestMode.Auto,
                    HarvestPath = "..\\outside\\harvest.wxs"
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec, null));
            Assert.Contains("outside ProjectRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenInstallerReferencesUnknownTarget()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "svc.msi",
                    PrepareFromTarget = "missing"
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("PrepareFromTarget", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareMsiPackage_CopiesPayloadAndWritesManifest()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "publish", "svc")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");
            Directory.CreateDirectory(Path.Combine(outputDir, "data"));
            File.WriteAllText(Path.Combine(outputDir, "data", "settings.json"), "{ }");

            var stagingPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "prepare.manifest.json");

            var step = new DotNetPublishStep
            {
                Key = "msi.prepare:svc.msi:svc:net10.0:win-x64:Portable",
                Kind = DotNetPublishStepKind.MsiPrepare,
                Title = "MSI prepare",
                InstallerId = "svc.msi",
                TargetName = "svc",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                StagingPath = stagingPath,
                ManifestPath = manifestPath
            };

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Target = "svc",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.Portable,
                    OutputDir = outputDir
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("PrepareMsiPackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { plan, artefacts, step }) as DotNetPublishMsiPrepareResult;
            Assert.NotNull(result);
            Assert.Equal("svc.msi", result!.InstallerId);
            Assert.Equal(Path.GetFullPath(stagingPath), Path.GetFullPath(result.StagingDir));
            Assert.True(File.Exists(Path.Combine(result.StagingDir, "Svc.exe")));
            Assert.True(File.Exists(Path.Combine(result.StagingDir, "data", "settings.json")));
            Assert.True(File.Exists(result.ManifestPath));

            var manifest = File.ReadAllText(result.ManifestPath);
            Assert.Contains("\"installerId\": \"svc.msi\"", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"target\": \"svc\"", manifest, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareMsiPackage_WritesHarvestFragment_WhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "publish", "svc")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");
            Directory.CreateDirectory(Path.Combine(outputDir, "data"));
            File.WriteAllText(Path.Combine(outputDir, "data", "settings.json"), "{ }");

            var stagingPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "prepare.manifest.json");
            var harvestPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "harvest.wxs");

            var step = new DotNetPublishStep
            {
                Key = "msi.prepare:svc.msi:svc:net10.0:win-x64:Portable",
                Kind = DotNetPublishStepKind.MsiPrepare,
                Title = "MSI prepare",
                InstallerId = "svc.msi",
                TargetName = "svc",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                StagingPath = stagingPath,
                ManifestPath = manifestPath,
                HarvestPath = harvestPath,
                HarvestDirectoryRefId = "INSTALLFOLDER",
                HarvestComponentGroupId = "Harvest_svc_msi"
            };

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Target = "svc",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.Portable,
                    OutputDir = outputDir
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("PrepareMsiPackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { plan, artefacts, step }) as DotNetPublishMsiPrepareResult;
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(harvestPath), Path.GetFullPath(result!.HarvestPath!));
            Assert.Equal("INSTALLFOLDER", result.HarvestDirectoryRefId);
            Assert.Equal("Harvest_svc_msi", result.HarvestComponentGroupId);
            Assert.True(File.Exists(result.HarvestPath));

            var wxs = File.ReadAllText(result.HarvestPath!);
            Assert.Contains("<Wix", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<DirectoryRef Id=\"INSTALLFOLDER\">", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<ComponentGroup Id=\"Harvest_svc_msi\">", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Svc.exe", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("settings.json", wxs, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareMsiPackage_ExcludesConfiguredHarvestPatterns()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "publish", "svc")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "Install-Service.ps1"), "install");
            Directory.CreateDirectory(Path.Combine(outputDir, "data"));
            File.WriteAllText(Path.Combine(outputDir, "data", "settings.json"), "{ }");
            Directory.CreateDirectory(Path.Combine(outputDir, "data", "deep"));
            File.WriteAllText(Path.Combine(outputDir, "data", "deep", "symbols.pdb"), "symbols");
            File.WriteAllText(Path.Combine(outputDir, "data", "deep", "createdump.exe"), "diag");

            var stagingPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "prepare.manifest.json");
            var harvestPath = Path.Combine(root, "Artifacts", "Msi", "svc.msi", "harvest.wxs");

            var step = new DotNetPublishStep
            {
                Key = "msi.prepare:svc.msi:svc:net10.0:win-x64:Portable",
                Kind = DotNetPublishStepKind.MsiPrepare,
                Title = "MSI prepare",
                InstallerId = "svc.msi",
                TargetName = "svc",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                StagingPath = stagingPath,
                ManifestPath = manifestPath,
                HarvestPath = harvestPath,
                HarvestDirectoryRefId = "INSTALLFOLDER",
                HarvestComponentGroupId = "Harvest_svc_msi"
            };

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "svc.msi",
                        HarvestExcludePatterns = new[] { "Svc.exe", "**/Install-Service.ps1", "**/*.pdb", "createdump.exe" }
                    }
                }
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Target = "svc",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.Portable,
                    OutputDir = outputDir
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("PrepareMsiPackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { plan, artefacts, step }) as DotNetPublishMsiPrepareResult;
            Assert.NotNull(result);

            var wxs = File.ReadAllText(result!.HarvestPath!);
            Assert.DoesNotContain("Svc.exe", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Install-Service.ps1", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("symbols.pdb", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("createdump.exe", wxs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("settings.json", wxs, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishSpec CreateBaseSpec(string root, string projectPath)
    {
        return new DotNetPublishSpec
        {
            DotNet = new DotNetPublishDotNetOptions
            {
                ProjectRoot = root,
                Restore = false,
                Build = false,
                Runtimes = new[] { "win-x64" }
            },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = "svc",
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        UseStaging = false
                    }
                }
            }
        };
    }

    private static string CreateProject(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return fullPath;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
