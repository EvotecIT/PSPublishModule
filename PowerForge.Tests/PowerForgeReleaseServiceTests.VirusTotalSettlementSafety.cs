namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ShouldPublishVirusTotalMonitorFromCheckpoint_BuildOnlyOutputs_ReturnsFalse()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Packages = new ProjectBuildConfiguration
            {
                PublishNuget = false,
                PublishGitHub = false
            },
            VirusTotal = new PowerForgeVirusTotalOptions { Enabled = true }
        };
        var result = new PowerForgeReleaseResult
        {
            Packages = new ProjectBuildHostExecutionResult { Success = true },
            ReleaseAssetEntries =
            [
                new PowerForgeReleaseAssetEntry
                {
                    Path = "Example.1.2.3.nupkg",
                    Category = PowerForgeReleaseAssetCategory.Package,
                    IsFinalPackageOutput = true
                }
            ]
        };

        Assert.False(PowerForgeReleaseService.ShouldPublishVirusTotalMonitorFromCheckpoint(spec, result));
    }

    [Fact]
    public void ShouldPublishVirusTotalMonitorFromCheckpoint_ModulePackagePublisher_ReturnsTrue()
    {
        var spec = new PowerForgeReleaseSpec
        {
            VirusTotal = new PowerForgeVirusTotalOptions { Enabled = true }
        };
        var result = new PowerForgeReleaseResult
        {
            ModulePackagePlans =
            [
                new PowerForgeModulePackageReleaseCheckpoint
                {
                    PublishNuget = true
                }
            ]
        };

        Assert.True(PowerForgeReleaseService.ShouldPublishVirusTotalMonitorFromCheckpoint(spec, result));
    }

    [Fact]
    public void ShouldPublishVirusTotalMonitorFromCheckpoint_StudioModulePublisher_ReturnsTrue()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions(),
            VirusTotal = new PowerForgeVirusTotalOptions { Enabled = true }
        };
        var result = new PowerForgeReleaseResult
        {
            ModulePlan = new PowerForgeModuleReleasePlanSummary
            {
                RunMode = ConfigurationGateMode.Build
            }
        };

        Assert.True(PowerForgeReleaseService.ShouldPublishVirusTotalMonitorFromCheckpoint(
            spec,
            result,
            modulePublisherActive: true));
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_MatchingDestinationFromDifferentAggregateVersion_ResumesItem()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            File.WriteAllText(receiptPath, """
                {
                  "schemaVersion": 1,
                  "provider": "VirusTotal Monitor",
                  "project": "Example",
                  "version": "9.9.9",
                  "artifacts": [
                    {
                      "kind": "MsiPackage",
                      "destinationPath": "/Example/1.2.3/MsiPackage/Example.msi",
                      "monitorId": "existing-item",
                      "verificationStatus": "Verified"
                    }
                  ]
                }
                """);
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
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success);
            var artifact = Assert.Single(Assert.IsType<VirusTotalMonitorPublishRequest>(captured).Artifacts);
            Assert.Equal("existing-item", artifact.ExistingItemId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(
        "[{\"destinationPath\":\"/Example/1.2.3/MsiPackage/Example.msi\"}]",
        "destinationPath and monitorId")]
    [InlineData(
        "[{\"destinationPath\":\"/Example/1.2.3/MsiPackage/Example.msi\",\"monitorId\":\"first\"},{\"destinationPath\":\"/Example/1.2.3/MsiPackage/Example.msi\",\"monitorId\":\"second\"}]",
        "conflicting item ids")]
    public void PublishBuiltReleaseOutputs_InvalidArtifactReceipt_BlocksPrimaryPublisher(
        string artifactsJson,
        string expectedMessage)
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            File.WriteAllText(receiptPath, $$"""
                {
                  "schemaVersion": 1,
                  "provider": "VirusTotal Monitor",
                  "project": "Example",
                  "version": "1.2.3",
                  "artifacts": {{artifactsJson}}
                }
                """);
            var githubCalled = false;
            var monitorCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ =>
                {
                    githubCalled = true;
                    return new GitHubReleasePublishResult { Succeeded = true };
                },
                publishVirusTotalMonitor: (_, _) =>
                {
                    monitorCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.Packages = null;
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true,
                Owner = "EvotecIT",
                Repository = "Example",
                Token = "test-token"
            };
            spec.VirusTotal!.ReceiptPath = receiptPath;

            var exception = Assert.Throws<InvalidDataException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(githubCalled);
            Assert.False(monitorCalled);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task PublishBuiltReleaseOutputs_ConcurrentReceiptUse_IsRejectedBeforeSecondUpload()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            using var publisherEntered = new ManualResetEventSlim();
            using var releasePublisher = new ManualResetEventSlim();
            var uploadCount = 0;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    Interlocked.Increment(ref uploadCount);
                    publisherEntered.Set();
                    Assert.True(releasePublisher.Wait(TimeSpan.FromSeconds(10)));
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;

            var first = Task.Run(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath)));
            Assert.True(publisherEntered.Wait(TimeSpan.FromSeconds(10)));

            var exception = Assert.Throws<IOException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains("already in use", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, Volatile.Read(ref uploadCount));
            releasePublisher.Set();
            Assert.True((await first).Success);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
