namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void GetDotNetGitHubConfigurationAssets_StagesPortableOrdinaryConfigurationInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string configDirectory = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            string releaseConfig = Path.Combine(root, "release.json");
            string publishConfig = Path.Combine(configDirectory, "dotnetpublish.json");
            File.WriteAllText(releaseConfig, "{ \"Tools\": { \"DotNetPublishConfigPath\": \"Build/dotnetpublish.json\" } }");
            File.WriteAllText(publishConfig, "{ \"Targets\": [] }");
            var plan = new DotNetPublishPlan
            {
                ConfigurationInputPaths = new[] { releaseConfig, publishConfig },
                GeneratedConfigurationInputPaths = Array.Empty<string>()
            };

            string stagingDirectory = Path.Combine(root, "staged");
            string[] assets = PowerForgeReleaseService.GetDotNetGitHubConfigurationAssets(plan, stagingDirectory);

            Assert.Equal(2, assets.Length);
            Assert.All(assets, path => Assert.Equal(stagingDirectory, Path.GetDirectoryName(path)));
            string stagedRelease = Assert.Single(assets, path => Path.GetFileName(path) == "release.json");
            string stagedPublish = Assert.Single(assets, path => Path.GetFileName(path).StartsWith(".release.dotnetpublish.", StringComparison.Ordinal));
            DotNetPublishConfiguredSpec configured = DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputs(stagedRelease);
            Assert.Equal(new[] { stagedRelease, stagedPublish }, configured.InputPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("{ \"Targets\": [] }", File.ReadAllText(stagedPublish));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateConfigurationAssetEntries_IncludesPortableOrdinaryConfigurationInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string configDirectory = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            string releaseConfig = Path.Combine(root, "release.json");
            string publishConfig = Path.Combine(configDirectory, "dotnetpublish.json");
            string checksums = Path.Combine(root, "Artifacts", "SHA256SUMS.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(checksums)!);
            File.WriteAllText(releaseConfig, "{ \"Tools\": { \"DotNetPublishConfigPath\": \"Build/dotnetpublish.json\" } }");
            File.WriteAllText(publishConfig, "{ \"Targets\": [] }");
            var plan = new DotNetPublishPlan
            {
                ConfigurationInputPaths = new[] { releaseConfig, publishConfig }
            };

            PowerForgeReleaseAssetEntry[] assets = PowerForgeReleaseService.CreateConfigurationAssetEntries(
                plan,
                checksums).ToArray();

            Assert.Equal(2, assets.Length);
            Assert.All(assets, asset => Assert.Equal(PowerForgeReleaseAssetCategory.Metadata, asset.Category));
            Assert.Contains(assets, asset => Path.GetFileName(asset.Path) == "release.json");
            Assert.Contains(assets, asset => Path.GetFileName(asset.Path).StartsWith(".release.dotnetpublish.", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteReleaseChecksums_UsesFlattenedGitHubAssetNamesForConfigurationEvidence()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string nested = Directory.CreateDirectory(Path.Combine(root, "release.configuration-assets")).FullName;
            string configuration = Path.Combine(nested, "release.json");
            string artifact = Path.Combine(root, "Sample.zip");
            string checksums = Path.Combine(root, "SHA256SUMS.txt");
            File.WriteAllText(configuration, "configuration");
            File.WriteAllText(artifact, "artifact");
            var result = new PowerForgeReleaseResult
            {
                ReleaseAssets = new[] { configuration, artifact }
            };

            PowerForgeReleaseService.WriteReleaseChecksums(result, checksums);

            string[] lines = File.ReadAllLines(checksums);
            Assert.Contains(lines, line => line.EndsWith(" *release.json", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.EndsWith(" *Sample.zip", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("release.configuration-assets/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteReleaseChecksums_RejectsDestinationThatCollidesWithReleaseInput(bool samePath)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string assetDirectory = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            string asset = Path.Combine(assetDirectory, "SHA256SUMS.txt");
            string checksums = samePath ? asset : Path.Combine(root, "SHA256SUMS.txt");
            File.WriteAllText(asset, "original asset");
            var result = new PowerForgeReleaseResult { ReleaseAssets = new[] { asset } };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.WriteReleaseChecksums(result, checksums));

            Assert.Contains(
                samePath ? "collides" : "unique file names",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("original asset", File.ReadAllText(asset));
            if (!samePath)
                Assert.False(File.Exists(checksums));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryBuildDotNetGitHubRunnableAssets_InstallerOrStoreOnlyTarget_IsPublishable(bool installer)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string asset = Path.Combine(root, installer ? "Sample.msi" : "Sample.msixupload");
            File.WriteAllText(asset, "package");
            var target = new DotNetPublishTargetPlan { Name = "Sample" };
            var plan = new DotNetPublishPlan { Targets = new[] { target } };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                MsiBuilds = installer
                    ? new[] { new DotNetPublishMsiBuildResult { Target = "Sample", OutputFiles = new[] { asset } } }
                    : Array.Empty<DotNetPublishMsiBuildResult>(),
                StorePackages = installer
                    ? Array.Empty<DotNetPublishStorePackageResult>()
                    : new[] { new DotNetPublishStorePackageResult { Target = "Sample", UploadFiles = new[] { asset } } }
            };

            bool success = PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(
                plan,
                target,
                result,
                out List<string> assets,
                out _,
                out _,
                out string? error);

            Assert.True(success, error);
            Assert.Equal(Path.GetFullPath(asset), Assert.Single(assets));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void TryResolveDotNetGitHubArtefactPath_BundleUsesOwnPackagingMode(
        bool targetZip,
        bool bundleZip,
        bool expectZip)
    {
        var target = new DotNetPublishTargetPlan
        {
            Name = "Sample",
            Publish = new DotNetPublishPublishOptions { Zip = targetZip }
        };
        var plan = new DotNetPublishPlan
        {
            Targets = new[] { target },
            Bundles = new[]
            {
                new DotNetPublishBundlePlan
                {
                    Id = "Desktop",
                    PrepareFromTarget = "Sample",
                    Zip = bundleZip
                }
            }
        };
        var artefact = new DotNetPublishArtefactResult
        {
            Category = DotNetPublishArtefactCategory.Bundle,
            Target = "Sample",
            BundleId = "Desktop",
            ZipPath = "bundle.zip",
            ExePath = "bundle.exe"
        };

        bool success = PowerForgeReleaseService.TryResolveDotNetGitHubArtefactPath(
            plan,
            target,
            artefact,
            out string? path,
            out bool direct,
            out string? error);

        Assert.True(success, error);
        Assert.Equal(expectZip ? "bundle.zip" : "bundle.exe", path);
        Assert.Equal(!expectZip, direct);
    }

    [Fact]
    public void TryBuildDotNetGitHubRunnableAssets_DirectMatrixWithSharedBasenames_StagesUniqueAssets()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string firstDirectory = Directory.CreateDirectory(Path.Combine(root, "net8")).FullName;
            string secondDirectory = Directory.CreateDirectory(Path.Combine(root, "net10")).FullName;
            string first = Path.Combine(firstDirectory, "Sample.exe");
            string second = Path.Combine(secondDirectory, "Sample.exe");
            File.WriteAllText(first, "net8");
            File.WriteAllText(second, "net10");
            var target = new DotNetPublishTargetPlan
            {
                Name = "Sample",
                Publish = new DotNetPublishPublishOptions { Zip = false }
            };
            var plan = new DotNetPublishPlan { Targets = new[] { target } };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                Artefacts = new[]
                {
                    new DotNetPublishArtefactResult
                    {
                        Category = DotNetPublishArtefactCategory.Publish,
                        Target = "Sample",
                        Framework = "net8.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat,
                        ExePath = first
                    },
                    new DotNetPublishArtefactResult
                    {
                        Category = DotNetPublishArtefactCategory.Publish,
                        Target = "Sample",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat,
                        ExePath = second
                    }
                }
            };

            bool success = PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(
                plan,
                target,
                result,
                out List<string> assets,
                out _,
                out _,
                out string? error);

            Assert.True(success, error);
            Assert.Equal(2, assets.Count);
            Assert.Equal(2, assets.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(assets, path => Assert.Contains("Sample.release-assets", path, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { "net10", "net8" }, assets.Select(File.ReadAllText).OrderBy(value => value).ToArray());
            string catalog = ModulePublisher.WriteGitHubChecksumCatalog(
                Path.Combine(root, "Sample.SHA256SUMS.txt"),
                assets);
            Assert.Equal(2, File.ReadAllLines(catalog).Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryBuildDotNetGitHubRunnableAssets_SignedDirectMatrixStagesPublisherEvidenceWithEachAlias()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string firstDirectory = Directory.CreateDirectory(Path.Combine(root, "net8")).FullName;
            string secondDirectory = Directory.CreateDirectory(Path.Combine(root, "net10")).FullName;
            string first = Path.Combine(firstDirectory, "Sample.exe");
            string second = Path.Combine(secondDirectory, "Sample.exe");
            foreach (string executable in new[] { first, second })
            {
                File.WriteAllText(executable, Path.GetFileName(Path.GetDirectoryName(executable)));
                File.WriteAllText(
                    executable + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                    "signed inventory");
                File.WriteAllText(
                    executable + PowerForgePortablePayloadInventory.DirectSignatureSuffix,
                    "inventory signature");
            }
            var target = new DotNetPublishTargetPlan
            {
                Name = "Sample",
                Publish = new DotNetPublishPublishOptions { Zip = false }
            };
            var plan = new DotNetPublishPlan { Targets = new[] { target } };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                Artefacts = new[]
                {
                    new DotNetPublishArtefactResult
                    {
                        Category = DotNetPublishArtefactCategory.Publish,
                        Target = "Sample",
                        Framework = "net8.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat,
                        OutputDir = firstDirectory,
                        PublishDir = firstDirectory,
                        ExePath = first,
                        SignedFiles = 1
                    },
                    new DotNetPublishArtefactResult
                    {
                        Category = DotNetPublishArtefactCategory.Publish,
                        Target = "Sample",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat,
                        OutputDir = secondDirectory,
                        PublishDir = secondDirectory,
                        ExePath = second,
                        SignedFiles = 1
                    }
                }
            };

            bool success = PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(
                plan,
                target,
                result,
                out List<string> assets,
                out _,
                out _,
                out string? error);

            Assert.True(success, error);
            Assert.Equal(6, assets.Count);
            Assert.Equal(6, assets.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(assets, path => Assert.Contains("Sample.release-assets", path, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, assets.Count(path => path.EndsWith(PowerForgePortablePayloadInventory.DirectInventorySuffix, StringComparison.Ordinal)));
            Assert.Equal(2, assets.Count(path => path.EndsWith(PowerForgePortablePayloadInventory.DirectSignatureSuffix, StringComparison.Ordinal)));
            string catalog = ModulePublisher.WriteGitHubChecksumCatalog(
                Path.Combine(root, "Sample.SHA256SUMS.txt"),
                assets);
            Assert.Equal(6, File.ReadAllLines(catalog).Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(DotNetPublishArtefactCategory.Publish, DotNetPublishStyle.FrameworkDependent, "Sample.dll")]
    [InlineData(DotNetPublishArtefactCategory.Publish, DotNetPublishStyle.PortableCompat, "appsettings.json")]
    [InlineData(DotNetPublishArtefactCategory.Publish, DotNetPublishStyle.PortableCompat, "settings.xml")]
    [InlineData(DotNetPublishArtefactCategory.Bundle, DotNetPublishStyle.PortableCompat, "runtime-data.json")]
    public void TryBuildDotNetGitHubRunnableAssets_DirectMultiFileOutputRequiresArchive(
        DotNetPublishArtefactCategory category,
        DotNetPublishStyle style,
        string companionName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            string executable = Path.Combine(outputDirectory, "Sample.exe");
            File.WriteAllText(executable, "executable");
            File.WriteAllText(Path.Combine(outputDirectory, companionName), "runtime payload");
            var target = new DotNetPublishTargetPlan
            {
                Name = "Sample",
                Publish = new DotNetPublishPublishOptions { Zip = false }
            };
            var plan = new DotNetPublishPlan
            {
                Targets = new[] { target },
                Bundles = category == DotNetPublishArtefactCategory.Bundle
                    ? new[]
                    {
                        new DotNetPublishBundlePlan
                        {
                            Id = "Desktop",
                            PrepareFromTarget = "Sample",
                            Zip = false
                        }
                    }
                    : Array.Empty<DotNetPublishBundlePlan>()
            };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                Artefacts = new[]
                {
                    new DotNetPublishArtefactResult
                    {
                        Category = category,
                        Target = "Sample",
                        BundleId = category == DotNetPublishArtefactCategory.Bundle ? "Desktop" : null,
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = style,
                        OutputDir = outputDirectory,
                        PublishDir = outputDirectory,
                        ExePath = executable,
                        Files = 2
                    }
                }
            };

            bool success = PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(
                plan,
                target,
                result,
                out List<string> assets,
                out _,
                out _,
                out string? error);

            Assert.False(success);
            Assert.Empty(assets);
            Assert.Contains("ZIP packaging", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryBuildDotNetGitHubRunnableAssets_DirectSingleFileAllowsSymbolAndDocumentationFiles()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            string executable = Path.Combine(outputDirectory, "Sample.exe");
            File.WriteAllText(executable, "executable");
            File.WriteAllText(Path.Combine(outputDirectory, "Sample.pdb"), "symbols");
            File.WriteAllText(Path.Combine(outputDirectory, "Sample.xml"), "documentation");
            var target = new DotNetPublishTargetPlan
            {
                Name = "Sample",
                Publish = new DotNetPublishPublishOptions { Zip = false }
            };
            var plan = new DotNetPublishPlan { Targets = new[] { target } };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                Artefacts = new[]
                {
                    new DotNetPublishArtefactResult
                    {
                        Category = DotNetPublishArtefactCategory.Publish,
                        Target = "Sample",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat,
                        OutputDir = outputDirectory,
                        PublishDir = outputDirectory,
                        ExePath = executable,
                        Files = 3
                    }
                }
            };

            bool success = PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(
                plan,
                target,
                result,
                out List<string> assets,
                out _,
                out _,
                out string? error);

            Assert.True(success, error);
            Assert.Equal(executable, Assert.Single(assets));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
