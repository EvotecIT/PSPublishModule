using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBundleTests
{
    [Fact]
    public void Plan_BindsInstallerPrepareFromBundleId_ToMsiPrepareStep()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Bundles = new[]
            {
                new DotNetPublishBundle
                {
                    Id = "portable",
                    PrepareFromTarget = "app"
                }
            };
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    PrepareFromBundleId = "portable",
                    InstallerProjectPath = installer
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var msiPrepare = Assert.Single(plan.Steps, step => step.Kind == DotNetPublishStepKind.MsiPrepare);
            Assert.Equal("portable", msiPrepare.BundleId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildBundle_AppliesPostProcessArchiveDeleteAndMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.exe"), "app");
            File.WriteAllText(Path.Combine(publishDir, "createdump.exe"), "diag");
            File.WriteAllText(Path.Combine(publishDir, "notes.pdb"), "symbols");

            var pluginOne = Directory.CreateDirectory(Path.Combine(publishDir, "plugins", "Plugin.One")).FullName;
            var pluginTwo = Directory.CreateDirectory(Path.Combine(publishDir, "plugins", "Plugin.Two")).FullName;
            File.WriteAllText(Path.Combine(pluginOne, "plugin.dll"), "one");
            File.WriteAllText(Path.Combine(pluginTwo, "plugin.dll"), "two");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Bundles = new[]
                {
                    new DotNetPublishBundlePlan
                    {
                        Id = "portable",
                        PrepareFromTarget = "app",
                        PostProcess = new DotNetPublishBundlePostProcessOptions
                        {
                            ArchiveDirectories = new[]
                            {
                                new DotNetPublishBundleArchiveRule
                                {
                                    Path = "plugins",
                                    Mode = DotNetPublishBundleArchiveMode.ChildDirectories,
                                    ArchiveNameTemplate = "{name}.ix-plugin.zip",
                                    DeleteSource = true
                                }
                            },
                            DeletePatterns = new[] { "**/*.pdb", "**/createdump.exe" },
                            Metadata = new DotNetPublishBundleMetadataOptions
                            {
                                Path = "portable-bundle.json",
                                Properties = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["primaryExecutable"] = "App.exe",
                                    ["bundleName"] = "{bundleId}"
                                }
                            }
                        }
                    }
                }
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = publishDir,
                    PublishDir = publishDir
                }
            };

            var outputDir = Path.Combine(root, "Artifacts", "Bundles", "portable");
            var step = new DotNetPublishStep
            {
                Key = "bundle:portable:app:net10.0:win-x64:PortableCompat",
                Kind = DotNetPublishStepKind.Bundle,
                BundleId = "portable",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                BundleOutputPath = outputDir
            };

            var result = InvokeBuildBundle(plan, artefacts, step);

            Assert.NotNull(result);
            Assert.True(File.Exists(Path.Combine(outputDir, "App.exe")));
            Assert.False(File.Exists(Path.Combine(outputDir, "createdump.exe")));
            Assert.False(File.Exists(Path.Combine(outputDir, "notes.pdb")));

            var pluginZipOne = Path.Combine(outputDir, "plugins", "Plugin.One.ix-plugin.zip");
            var pluginZipTwo = Path.Combine(outputDir, "plugins", "Plugin.Two.ix-plugin.zip");
            Assert.True(File.Exists(pluginZipOne));
            Assert.True(File.Exists(pluginZipTwo));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "plugins", "Plugin.One")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "plugins", "Plugin.Two")));

            using (var archive = ZipFile.OpenRead(pluginZipOne))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("plugin.dll", StringComparison.OrdinalIgnoreCase));
            }

            var metadataPath = Path.Combine(outputDir, "portable-bundle.json");
            Assert.True(File.Exists(metadataPath));
            var metadata = File.ReadAllText(metadataPath);
            Assert.Contains("\"bundleId\": \"portable\"", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"primaryExecutable\": \"App.exe\"", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"bundleName\": \"portable\"", metadata, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildBundle_RejectsPostProcessPathsOutsideBundleRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.exe"), "app");

            var outputDir = Path.Combine(root, "Artifacts", "Bundles", "portable");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Bundles = new[]
                {
                    new DotNetPublishBundlePlan
                    {
                        Id = "portable",
                        PrepareFromTarget = "app",
                        PostProcess = new DotNetPublishBundlePostProcessOptions
                        {
                            Metadata = new DotNetPublishBundleMetadataOptions
                            {
                                Path = "../portable_sibling/outside.json"
                            }
                        }
                    }
                }
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = publishDir,
                    PublishDir = publishDir
                }
            };

            var step = new DotNetPublishStep
            {
                Key = "bundle:portable:app:net10.0:win-x64:PortableCompat",
                Kind = DotNetPublishStepKind.Bundle,
                BundleId = "portable",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                BundleOutputPath = outputDir
            };

            var ex = Assert.Throws<InvalidOperationException>(() => InvokeBuildBundle(plan, artefacts, step));
            Assert.Contains("metadata path", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareMsiPackage_UsesBundleArtefact_WhenInstallerPrepareFromBundleIdConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var rawPublishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "raw")).FullName;
            var bundleDir = Directory.CreateDirectory(Path.Combine(root, "publish", "bundle")).FullName;
            File.WriteAllText(Path.Combine(rawPublishDir, "Raw.exe"), "raw");
            File.WriteAllText(Path.Combine(bundleDir, "Portable.exe"), "bundle");

            var stagingPath = Path.Combine(root, "Artifacts", "Msi", "app.msi", "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "Msi", "app.msi", "prepare.manifest.json");

            var step = new DotNetPublishStep
            {
                Key = "msi.prepare:app.msi:app:net10.0:win-x64:PortableCompat",
                Kind = DotNetPublishStepKind.MsiPrepare,
                Title = "MSI prepare",
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                StagingPath = stagingPath,
                ManifestPath = manifestPath
            };

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app.msi",
                        PrepareFromTarget = "app",
                        PrepareFromBundleId = "portable"
                    }
                }
            };

            var artefacts = new[]
            {
                new DotNetPublishArtefactResult
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = rawPublishDir,
                    PublishDir = rawPublishDir
                },
                new DotNetPublishArtefactResult
                {
                    Category = DotNetPublishArtefactCategory.Bundle,
                    BundleId = "portable",
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = bundleDir,
                    PublishDir = bundleDir
                }
            };

            var result = InvokePrepareMsiPackage(plan, artefacts, step);

            Assert.NotNull(result);
            Assert.Equal(DotNetPublishArtefactCategory.Bundle, result!.SourceCategory);
            Assert.Equal("portable", result.BundleId);
            Assert.Equal(Path.GetFullPath(bundleDir), Path.GetFullPath(result.SourceOutputDir));
            Assert.True(File.Exists(Path.Combine(result.StagingDir, "Portable.exe")));
            Assert.False(File.Exists(Path.Combine(result.StagingDir, "Raw.exe")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishArtefactResult InvokeBuildBundle(
        DotNetPublishPlan plan,
        DotNetPublishArtefactResult[] artefacts,
        DotNetPublishStep step)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var result = runner.BuildBundle(plan, artefacts, step);
        Assert.NotNull(result);
        return result!;
    }

    private static DotNetPublishMsiPrepareResult InvokePrepareMsiPackage(
        DotNetPublishPlan plan,
        DotNetPublishArtefactResult[] artefacts,
        DotNetPublishStep step)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var result = runner.PrepareMsiPackage(plan, artefacts, step);
        Assert.NotNull(result);
        return result!;
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
                    Name = "app",
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        Styles = new[] { DotNetPublishStyle.PortableCompat },
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
