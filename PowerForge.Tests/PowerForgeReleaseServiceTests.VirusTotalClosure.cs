using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_CheckpointBuildWithVirusTotalModuleKind_CapturesProducedModuleProvenance()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            var archivePath = Path.Combine(root, "ExampleModule.v1.2.3.zip");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                _ =>
                {
                    using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                    archive.CreateEntry("ExampleModule/ExampleModule.psd1");
                    archive.CreateEntry("ExampleModule/ExampleModule.psm1");
                });
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root,
                    ScriptPath = scriptPath,
                    ArtifactPaths = [archivePath]
                },
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ApiKeyEnvName = "POWERFORGE_TEST_UNUSED_" + Guid.NewGuid().ToString("N"),
                    ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
                }
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Build,
                    CaptureModuleArtifactProvenance = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(archivePath, result.ModuleProducedAssets, StringComparer.OrdinalIgnoreCase);
            Assert.True(Assert.Single(result.ReleaseAssetEntries).IsFinalPackageOutput);
            Assert.Null(result.VirusTotalMonitor);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ModuleVersionFallback_ReachesVirusTotalPublisher()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var archivePath = Path.Combine(root, "ExampleModule.v1.2.3.zip");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(archivePath, "checkpointed signed module");
            VirusTotalMonitorPublishRequest? captured = null;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (request, _) =>
                {
                    captured = request;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions { ModuleName = "ExampleModule" },
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "ExampleModule",
                    ApiKey = "test-key",
                    ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
                }
            };
            var builtResult = new PowerForgeReleaseResult
            {
                Success = true,
                ModulePlan = new PowerForgeModuleReleasePlanSummary { ModuleVersion = "1.2.3" },
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = archivePath,
                        Category = PowerForgeReleaseAssetCategory.Module,
                        IsFinalPackageOutput = true
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Contains("/1.2.3/", Assert.Single(captured!.Artifacts).DestinationPath, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_AssetVersionFallback_ReachesVirusTotalPublisher()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            VirusTotalMonitorPublishRequest? captured = null;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (request, _) =>
                {
                    captured = request;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "Example",
                    ApiKey = "test-key",
                    ArtifactKinds = [VirusTotalArtifactKind.MsiPackage]
                }
            };
            var builtResult = new PowerForgeReleaseResult
            {
                Success = true,
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = artifactPath,
                        Category = PowerForgeReleaseAssetCategory.Installer,
                        Version = "4.5.6",
                        IsFinalPackageOutput = true
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Contains("/4.5.6/", Assert.Single(captured!.Artifacts).DestinationPath, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_PublisherThrows_PersistsFailureReceipt()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "vt.json");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) => throw new InvalidOperationException("provider construction failed"));
            var spec = new PowerForgeReleaseSpec
            {
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "Example",
                    ApiKey = "test-key",
                    ArtifactKinds = [VirusTotalArtifactKind.MsiPackage],
                    ReceiptPath = receiptPath
                }
            };
            var builtResult = new PowerForgeReleaseResult
            {
                Success = true,
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = artifactPath,
                        Category = PowerForgeReleaseAssetCategory.Installer,
                        Version = "1.2.3",
                        IsFinalPackageOutput = true
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor).Success);
            Assert.Equal(receiptPath, result.VirusTotalMonitorReceiptPath);
            Assert.Contains("provider construction failed", File.ReadAllText(receiptPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_MixedVersionPackages_UsesEachArtifactVersion()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var firstPath = Path.Combine(root, "First.1.2.3.nupkg");
            var secondPath = Path.Combine(root, "Second.4.5.6.nupkg");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(firstPath, "first package");
            File.WriteAllText(secondPath, "second package");
            VirusTotalMonitorPublishRequest? captured = null;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (request, _) =>
                {
                    captured = request;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "Example",
                    ApiKey = "test-key",
                    ArtifactKinds = [VirusTotalArtifactKind.NuGetPackage]
                }
            };
            var builtResult = new PowerForgeReleaseResult
            {
                Success = true,
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = firstPath,
                        Category = PowerForgeReleaseAssetCategory.Package,
                        Version = "1.2.3",
                        IsFinalPackageOutput = true
                    },
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = secondPath,
                        Category = PowerForgeReleaseAssetCategory.Package,
                        Version = "4.5.6",
                        IsFinalPackageOutput = true
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Contains(captured!.Artifacts, artifact => artifact.DestinationPath.Contains("/1.2.3/", StringComparison.Ordinal));
            Assert.Contains(captured.Artifacts, artifact => artifact.DestinationPath.Contains("/4.5.6/", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }
}
