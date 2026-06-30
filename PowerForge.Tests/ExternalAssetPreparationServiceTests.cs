using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ExternalAssetPreparationServiceTests
{
    [Fact]
    public void Prepare_CopiesLocalFilesAndWritesShaManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "Source");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "sample payload");

            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    Version = "1.2.3",
                    OutputPath = "Artefacts/VendorTool",
                    Source = "https://example.test/vendor-tool",
                    License = "MIT",
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            Architecture = "x64",
                            FileName = "tool.zip",
                            Uri = sourceFile
                        }
                    }
                }
            };

            var result = service.Prepare(root, segment);

            var outputFile = Path.Combine(root, "Artefacts", "VendorTool", "tool.zip");
            var manifestPath = Path.Combine(root, "Artefacts", "VendorTool", "manifest.json");
            Assert.Equal("VendorTool", result.Name);
            Assert.True(File.Exists(outputFile));
            Assert.Equal("sample payload", File.ReadAllText(outputFile));
            Assert.True(File.Exists(manifestPath));

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            Assert.Equal("VendorTool", manifest.GetProperty("name").GetString());
            Assert.Equal("1.2.3", manifest.GetProperty("version").GetString());
            Assert.Equal("https://example.test/vendor-tool", manifest.GetProperty("source").GetString());
            Assert.Equal("MIT", manifest.GetProperty("license").GetString());
            var file = Assert.Single(manifest.GetProperty("files").EnumerateArray());
            Assert.Equal("win-x64", file.GetProperty("runtime").GetString());
            Assert.Equal("x64", file.GetProperty("architecture").GetString());
            Assert.Equal("tool.zip", file.GetProperty("path").GetString());
            Assert.Equal(ComputeSha256(outputFile), file.GetProperty("sha256").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_SkipDownloadFailsWhenFileIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    SkipDownload = true,
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "tool.zip",
                            Uri = "https://example.test/tool.zip"
                        }
                    }
                }
            };

            Assert.Throws<FileNotFoundException>(() => service.Prepare(root, segment));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_RejectsFileThatOverwritesGeneratedManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "Source");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "manifest.json");
            File.WriteAllText(sourceFile, "payload");

            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "manifest.json",
                            Uri = sourceFile
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(root, segment));
            Assert.Contains("manifest path", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_RejectsDuplicateOutputPathsWithoutOverwritingFirstFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "Source");
            Directory.CreateDirectory(sourceRoot);
            var firstSource = Path.Combine(sourceRoot, "first.zip");
            var secondSource = Path.Combine(sourceRoot, "second.zip");
            File.WriteAllText(firstSource, "first payload");
            File.WriteAllText(secondSource, "second payload");

            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "tool.zip",
                            Uri = firstSource
                        },
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-arm64",
                            FileName = "tool.zip",
                            Uri = secondSource
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(root, segment));
            Assert.Contains("already used", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("first payload", File.ReadAllText(Path.Combine(root, "Artefacts", "VendorTool", "tool.zip")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_RejectsOutputPathOutsideProjectRootBeforeMaterializingFiles()
    {
        var workspace = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(workspace, "Project");
            Directory.CreateDirectory(projectRoot);
            var sourceRoot = Path.Combine(workspace, "Source");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "payload");

            var outsideOutput = Path.Combine(workspace, "Outside", "VendorTool");
            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = outsideOutput,
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "tool.zip",
                            Uri = sourceFile
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(projectRoot, segment));
            Assert.Contains("OutputPath", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outsideOutput));
        }
        finally
        {
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Prepare_RejectsManifestPathOutsideProjectRootBeforeMaterializingFiles()
    {
        var workspace = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(workspace, "Project");
            Directory.CreateDirectory(projectRoot);
            var sourceRoot = Path.Combine(workspace, "Source");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "payload");

            var outsideManifest = Path.Combine(workspace, "Outside", "manifest.json");
            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    ManifestPath = outsideManifest,
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "tool.zip",
                            Uri = sourceFile
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(projectRoot, segment));
            Assert.Contains("ManifestPath", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outsideManifest));
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "Artefacts", "VendorTool")));
        }
        finally
        {
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Prepare_UsesFilesystemCasingForDuplicateOutputPaths()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "Source");
            Directory.CreateDirectory(sourceRoot);
            var firstSource = Path.Combine(sourceRoot, "first.zip");
            var secondSource = Path.Combine(sourceRoot, "second.zip");
            File.WriteAllText(firstSource, "first payload");
            File.WriteAllText(secondSource, "second payload");

            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "Tool.zip",
                            Uri = firstSource
                        },
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-arm64",
                            FileName = "tool.zip",
                            Uri = secondSource
                        }
                    }
                }
            };

            if (IsCaseSensitiveDirectory(root))
            {
                var result = service.Prepare(root, segment);
                Assert.Equal(2, result.Files.Length);
            }
            else
            {
                var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(root, segment));
                Assert.Contains("already used", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_StagesExternalAssetsWithoutStagingUnrelatedArtefacts()
    {
        var workspace = CreateTempDirectory();
        string? stagingPath = null;
        try
        {
            var projectRoot = Path.Combine(workspace, "TestModule");
            const string moduleName = "TestModule";
            WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var sourceRoot = Path.Combine(workspace, "Input");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "payload");

            var staleArtefactRoot = Path.Combine(projectRoot, "Artefacts", "Packed");
            Directory.CreateDirectory(staleArtefactRoot);
            File.WriteAllText(Path.Combine(staleArtefactRoot, "old.zip"), "old");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationExternalAssetSegment
                    {
                        Configuration = new ExternalAssetConfiguration
                        {
                            Name = "VendorTool",
                            OutputPath = "Artefacts/VendorTool",
                            Files = new[]
                            {
                                new ExternalAssetFileConfiguration
                                {
                                    Runtime = "win-x64",
                                    FileName = "tool.zip",
                                    Uri = sourceFile
                                }
                            }
                        }
                    }
                }
            };

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);
            stagingPath = result.BuildResult.StagingPath;

            Assert.True(File.Exists(Path.Combine(stagingPath, "Artefacts", "VendorTool", "tool.zip")));
            Assert.True(File.Exists(Path.Combine(stagingPath, "Artefacts", "VendorTool", "manifest.json")));
            Assert.False(File.Exists(Path.Combine(stagingPath, "Artefacts", "Packed", "old.zip")));
        }
        finally
        {
            TryDelete(stagingPath);
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Run_StagesOnlyPreparedExternalAssetFiles()
    {
        var workspace = CreateTempDirectory();
        string? stagingPath = null;
        try
        {
            var projectRoot = Path.Combine(workspace, "TestModule");
            const string moduleName = "TestModule";
            WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var sourceRoot = Path.Combine(workspace, "Input");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "payload");

            var outputRoot = Path.Combine(projectRoot, "Artefacts", "VendorTool");
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, "stale.zip"), "old");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationExternalAssetSegment
                    {
                        Configuration = new ExternalAssetConfiguration
                        {
                            Name = "VendorTool",
                            OutputPath = "Artefacts/VendorTool",
                            Files = new[]
                            {
                                new ExternalAssetFileConfiguration
                                {
                                    Runtime = "win-x64",
                                    FileName = "tool.zip",
                                    Uri = sourceFile
                                }
                            }
                        }
                    }
                }
            };

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);
            stagingPath = result.BuildResult.StagingPath;

            Assert.True(File.Exists(Path.Combine(stagingPath, "Artefacts", "VendorTool", "tool.zip")));
            Assert.True(File.Exists(Path.Combine(stagingPath, "Artefacts", "VendorTool", "manifest.json")));
            Assert.False(File.Exists(Path.Combine(stagingPath, "Artefacts", "VendorTool", "stale.zip")));
        }
        finally
        {
            TryDelete(stagingPath);
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Run_RemovesStaleExternalAssetFilesFromNonExcludedOutputPath()
    {
        var workspace = CreateTempDirectory();
        string? stagingPath = null;
        try
        {
            var projectRoot = Path.Combine(workspace, "TestModule");
            const string moduleName = "TestModule";
            WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var sourceRoot = Path.Combine(workspace, "Input");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "payload");

            var outputRoot = Path.Combine(projectRoot, "Resources", "VendorTool");
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, "stale.zip"), "old");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationExternalAssetSegment
                    {
                        Configuration = new ExternalAssetConfiguration
                        {
                            Name = "VendorTool",
                            OutputPath = "Resources/VendorTool",
                            Files = new[]
                            {
                                new ExternalAssetFileConfiguration
                                {
                                    Runtime = "win-x64",
                                    FileName = "tool.zip",
                                    Uri = sourceFile
                                }
                            }
                        }
                    }
                }
            };

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);
            stagingPath = result.BuildResult.StagingPath;

            Assert.True(File.Exists(Path.Combine(stagingPath, "Resources", "VendorTool", "tool.zip")));
            Assert.True(File.Exists(Path.Combine(stagingPath, "Resources", "VendorTool", "manifest.json")));
            Assert.False(File.Exists(Path.Combine(stagingPath, "Resources", "VendorTool", "stale.zip")));
        }
        finally
        {
            TryDelete(stagingPath);
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Run_RejectsExternalAssetOutputCollisionsAcrossBundlesBeforeMaterializingFiles()
    {
        var workspace = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(workspace, "TestModule");
            const string moduleName = "TestModule";
            WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var sourceRoot = Path.Combine(workspace, "Input");
            Directory.CreateDirectory(sourceRoot);
            var firstSource = Path.Combine(sourceRoot, "first.zip");
            var secondSource = Path.Combine(sourceRoot, "second.zip");
            File.WriteAllText(firstSource, "first");
            File.WriteAllText(secondSource, "second");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationExternalAssetSegment
                    {
                        Configuration = new ExternalAssetConfiguration
                        {
                            Name = "VendorTool",
                            OutputPath = "Resources/VendorTool",
                            Files = new[]
                            {
                                new ExternalAssetFileConfiguration
                                {
                                    Runtime = "win-x64",
                                    FileName = "tool.zip",
                                    Uri = firstSource
                                }
                            }
                        }
                    },
                    new ConfigurationExternalAssetSegment
                    {
                        Configuration = new ExternalAssetConfiguration
                        {
                            Name = "VendorToolDuplicate",
                            OutputPath = "Resources/VendorTool",
                            Files = new[]
                            {
                                new ExternalAssetFileConfiguration
                                {
                                    Runtime = "win-arm64",
                                    FileName = "tool.zip",
                                    Uri = secondSource
                                }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));

            Assert.Contains("output collision", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "Resources", "VendorTool")));
        }
        finally
        {
            TryDelete(workspace);
        }
    }

    [Fact]
    public void Prepare_UsesCaseSensitiveOutputContainmentOutsideWindows()
    {
        if (FrameworkCompatibility.IsWindows())
            return;

        var root = CreateTempDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "Source");
            Directory.CreateDirectory(sourceRoot);
            var sourceFile = Path.Combine(sourceRoot, "tool.zip");
            File.WriteAllText(sourceFile, "sample payload");

            var service = new ExternalAssetPreparationService(new NullLogger());
            var segment = new ConfigurationExternalAssetSegment
            {
                Configuration = new ExternalAssetConfiguration
                {
                    Name = "VendorTool",
                    OutputPath = "Artefacts/VendorTool",
                    Files = new[]
                    {
                        new ExternalAssetFileConfiguration
                        {
                            Runtime = "win-x64",
                            FileName = "tool.zip",
                            Path = "../artefacts/VendorTool/tool.zip",
                            Uri = sourceFile
                        }
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => service.Prepare(root, segment));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Schema_IncludesExternalAssetSegment()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Schemas", "powerforge.segments.schema.json"));
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var definitions = schema.RootElement.GetProperty("$defs");

        Assert.True(definitions.TryGetProperty("ExternalAssetConfiguration", out var configuration));
        Assert.True(configuration.GetProperty("properties").TryGetProperty("Enabled", out _));
        Assert.True(configuration.GetProperty("properties").TryGetProperty("Files", out _));
        Assert.True(definitions.TryGetProperty("ExternalAssetFileConfiguration", out var fileConfiguration));
        Assert.True(fileConfiguration.GetProperty("properties").TryGetProperty("Sha256", out _));
        Assert.True(definitions.TryGetProperty("ExternalAssetSegment", out _));

        var oneOf = definitions
            .GetProperty("ConfigurationSegment")
            .GetProperty("oneOf")
            .EnumerateArray();

        Assert.Contains(oneOf, item =>
            string.Equals(item.GetProperty("$ref").GetString(), "#/$defs/ExternalAssetSegment", StringComparison.Ordinal));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsCaseSensitiveDirectory(string directory)
    {
        var probeName = "powerforge-test-case-" + Guid.NewGuid().ToString("N") + "a.tmp";
        var probePath = Path.Combine(directory, probeName);
        var alternatePath = Path.Combine(directory, probeName.ToUpperInvariant());
        try
        {
            File.WriteAllText(probePath, string.Empty);
            return !File.Exists(alternatePath);
        }
        finally
        {
            TryDeleteFile(probePath);
            TryDeleteFile(alternatePath);
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

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path!, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
