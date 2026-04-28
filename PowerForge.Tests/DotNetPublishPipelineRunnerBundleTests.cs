using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBundleTests
{
    [Fact]
    public void ExamplePackageBundleMsi_DeserializesPackageLayoutPrimitives()
    {
        var repoRoot = RepoRootLocator.Find();
        var examplePath = Path.Combine(repoRoot, "Module", "Examples", "DotNetPublish", "Example.PackageBundleMsi.json");

        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(File.ReadAllText(examplePath), options);

        Assert.NotNull(spec);
        var bundle = Assert.Single(spec.Bundles);
        Assert.Equal("package", bundle.Id);
        Assert.Equal("Service", bundle.PrimarySubdirectory);
        Assert.Single(bundle.CopyItems);
        Assert.Single(bundle.ModuleIncludes);
        Assert.Single(bundle.GeneratedScripts);
        Assert.Equal("package", Assert.Single(spec.Installers).PrepareFromBundleId);
    }

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
    public void Plan_NormalizesBundlePackageLayoutPrimitives()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = CreateBaseSpec(root, app);
            spec.Profile = "release";
            spec.Profiles = new[]
            {
                new DotNetPublishProfile
                {
                    Name = "release",
                    Default = true,
                    Targets = new[] { "app" }
                }
            };
            spec.Bundles = new[]
            {
                new DotNetPublishBundle
                {
                    Id = "package",
                    PrepareFromTarget = "app",
                    PrimarySubdirectory = " Service ",
                    CopyItems = new[]
                    {
                        new DotNetPublishBundleCopyItem
                        {
                            SourcePath = " Build/README.md ",
                            DestinationPath = " README.md "
                        }
                    },
                    ModuleIncludes = new[]
                    {
                        new DotNetPublishBundleModuleInclude
                        {
                            ModuleName = " PowerTierBridge ",
                            SourcePath = " Artifacts/Modules/{moduleName} "
                        }
                    },
                    GeneratedScripts = new[]
                    {
                        new DotNetPublishBundleGeneratedScript
                        {
                            Template = "{{CommandName}}",
                            OutputPath = " Scripts/Install-Service.ps1 ",
                            Tokens = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["CommandName"] = "Install-TierBridgeService"
                            }
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var bundle = Assert.Single(plan.Bundles);

            Assert.Equal("Build/README.md", bundle.CopyItems[0].SourcePath);
            Assert.Equal("README.md", bundle.CopyItems[0].DestinationPath);
            Assert.Equal("Service", bundle.PrimarySubdirectory);
            Assert.Equal("PowerTierBridge", bundle.ModuleIncludes[0].ModuleName);
            Assert.Equal("Artifacts/Modules/{moduleName}", bundle.ModuleIncludes[0].SourcePath);
            Assert.Equal("Modules/{moduleName}", bundle.ModuleIncludes[0].DestinationPath);
            Assert.Equal("Scripts/Install-Service.ps1", bundle.GeneratedScripts[0].OutputPath);
            Assert.Equal("Install-TierBridgeService", bundle.GeneratedScripts[0].Tokens["CommandName"]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ResolvesBundlePostProcessSigningProfile()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = CreateBaseSpec(root, app);
            spec.SigningProfiles = new System.Collections.Generic.Dictionary<string, DotNetPublishSignOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["evotec"] = new DotNetPublishSignOptions
                {
                    Enabled = true,
                    IncludeDlls = true,
                    Thumbprint = "ABC123",
                    TimestampUrl = "http://timestamp.example"
                }
            };
            spec.Bundles = new[]
            {
                new DotNetPublishBundle
                {
                    Id = "package",
                    PrepareFromTarget = "app",
                    PostProcess = new DotNetPublishBundlePostProcessOptions
                    {
                        SignProfile = " evotec ",
                        SignPatterns = new[] { " **/*.ps1 ", "**/*.dll" },
                        SignOverrides = new DotNetPublishSignPatch
                        {
                            Description = "TierBridge Package"
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var postProcess = Assert.Single(plan.Bundles).PostProcess;

            Assert.NotNull(postProcess);
            Assert.Equal(new[] { "**/*.ps1", "**/*.dll" }, postProcess!.SignPatterns);
            Assert.NotNull(postProcess.Sign);
            Assert.True(postProcess.Sign!.Enabled);
            Assert.True(postProcess.Sign.IncludeDlls);
            Assert.Equal("ABC123", postProcess.Sign.Thumbprint);
            Assert.Equal("http://timestamp.example", postProcess.Sign.TimestampUrl);
            Assert.Equal("TierBridge Package", postProcess.Sign.Description);
            Assert.Null(postProcess.SignProfile);
            Assert.Null(postProcess.SignOverrides);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FindBundleSignTargets_MatchesNestedFilesAndExactPaths()
    {
        var root = CreateTempRoot();
        try
        {
            var bundleRoot = Directory.CreateDirectory(Path.Combine(root, "bundle")).FullName;
            File.WriteAllText(Path.Combine(bundleRoot, "App.exe"), "app");
            File.WriteAllText(Path.Combine(bundleRoot, "RootLibrary.dll"), "dll");
            File.WriteAllText(Path.Combine(bundleRoot, "README.md"), "readme");

            var scripts = Directory.CreateDirectory(Path.Combine(bundleRoot, "Scripts")).FullName;
            File.WriteAllText(Path.Combine(scripts, "Install-TierBridgeService.ps1"), "script");

            var module = Directory.CreateDirectory(Path.Combine(bundleRoot, "Modules", "PowerTierBridge")).FullName;
            File.WriteAllText(Path.Combine(module, "PowerTierBridge.psm1"), "module");
            File.WriteAllText(Path.Combine(module, "PowerTierBridge.psd1"), "manifest");

            var lib = Directory.CreateDirectory(Path.Combine(module, "Lib", "Core")).FullName;
            File.WriteAllText(Path.Combine(lib, "TierBridge.PowerShell.dll"), "dll");

            var targets = DotNetPublishPipelineRunner.FindBundleSignTargets(
                bundleRoot,
                new[] { "**/*.ps1", "**/*.psm1", "**/*.psd1", "**/*.dll", "App.exe" });

            Assert.Equal(6, targets.Length);
            Assert.Contains(targets, path => path.EndsWith("App.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, path => path.EndsWith("RootLibrary.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, path => path.EndsWith("Install-TierBridgeService.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, path => path.EndsWith("PowerTierBridge.psm1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, path => path.EndsWith("PowerTierBridge.psd1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, path => path.EndsWith("TierBridge.PowerShell.dll", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(targets, path => path.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));
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
    public void BuildBundle_CopiesPackageItemsModulesAndGeneratedScripts()
    {
        var root = CreateTempRoot();
        try
        {
            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.exe"), "app");

            var readmePath = Path.Combine(root, "Build", "README.package.md");
            Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
            File.WriteAllText(readmePath, "# Package");

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Modules", "PowerTierBridge")).FullName;
            File.WriteAllText(Path.Combine(moduleRoot, "PowerTierBridge.psd1"), "@{}");
            File.WriteAllText(Path.Combine(moduleRoot, "PowerTierBridge.psm1"), "");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Bundles = new[]
                {
                    new DotNetPublishBundlePlan
                    {
                        Id = "package",
                        PrepareFromTarget = "app",
                        PrimarySubdirectory = "Service",
                        CopyItems = new[]
                        {
                            new DotNetPublishBundleCopyItemPlan
                            {
                                SourcePath = "Build/README.package.md",
                                DestinationPath = "README.md"
                            }
                        },
                        ModuleIncludes = new[]
                        {
                            new DotNetPublishBundleModuleIncludePlan
                            {
                                ModuleName = "PowerTierBridge",
                                SourcePath = "Artifacts/Modules/{moduleName}",
                                DestinationPath = "Modules/{moduleName}"
                            }
                        },
                        GeneratedScripts = new[]
                        {
                            new DotNetPublishBundleGeneratedScriptPlan
                            {
                                Template = "Import-Module \"$PSScriptRoot\\Modules\\{{ModuleName}}\\{{ModuleName}}.psd1\" -Force\r\n{{CommandName}}\r\n",
                                OutputPath = "Install-Service.ps1",
                                Tokens = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["ModuleName"] = "PowerTierBridge",
                                    ["CommandName"] = "Install-TierBridgeService"
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

            var outputDir = Path.Combine(root, "Artifacts", "Bundles", "package");
            var step = new DotNetPublishStep
            {
                Key = "bundle:package:app:net10.0:win-x64:PortableCompat",
                Kind = DotNetPublishStepKind.Bundle,
                BundleId = "package",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                BundleOutputPath = outputDir
            };

            var result = InvokeBuildBundle(plan, artefacts, step);

            Assert.NotNull(result);
            Assert.True(File.Exists(Path.Combine(outputDir, "Service", "App.exe")));
            Assert.False(File.Exists(Path.Combine(outputDir, "App.exe")));
            Assert.True(File.Exists(Path.Combine(outputDir, "README.md")));
            Assert.True(File.Exists(Path.Combine(outputDir, "Modules", "PowerTierBridge", "PowerTierBridge.psd1")));

            var generatedScript = Path.Combine(outputDir, "Install-Service.ps1");
            Assert.True(File.Exists(generatedScript));
            var script = File.ReadAllText(generatedScript);
            Assert.Contains("PowerTierBridge.psd1", script, StringComparison.Ordinal);
            Assert.Contains("Install-TierBridgeService", script, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_PublishesBundleIncludesBeforeBundleStep()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var cli = CreateProject(root, "Cli/Cli.csproj");

            var spec = CreateBaseSpec(root, app);
            spec.Targets = new[]
            {
                spec.Targets[0],
                new DotNetPublishTarget
                {
                    Name = "cli",
                    ProjectPath = cli,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        Styles = new[] { DotNetPublishStyle.AotSpeed },
                        UseStaging = false
                    }
                }
            };
            spec.Bundles = new[]
            {
                new DotNetPublishBundle
                {
                    Id = "package",
                    PrepareFromTarget = "app",
                    Includes = new[]
                    {
                        new DotNetPublishBundleInclude
                        {
                            Target = "cli",
                            Style = DotNetPublishStyle.AotSpeed,
                            Subdirectory = "CLI"
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var keys = plan.Steps.Select(step => step.Key).ToArray();

            var cliPublish = Array.FindIndex(keys, key => key.StartsWith("publish:cli:", StringComparison.OrdinalIgnoreCase));
            var appPublish = Array.FindIndex(keys, key => key.StartsWith("publish:app:", StringComparison.OrdinalIgnoreCase));
            var bundle = Array.FindIndex(keys, key => key.StartsWith("bundle:package:", StringComparison.OrdinalIgnoreCase));

            Assert.True(cliPublish >= 0);
            Assert.True(appPublish >= 0);
            Assert.True(bundle > appPublish);
            Assert.True(bundle > cliPublish);
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
