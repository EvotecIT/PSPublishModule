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

    [Theory]
    [InlineData(DotNetPublishArtefactCategory.Publish, DotNetPublishStyle.FrameworkDependent, "Sample.dll")]
    [InlineData(DotNetPublishArtefactCategory.Publish, DotNetPublishStyle.PortableCompat, "appsettings.json")]
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
