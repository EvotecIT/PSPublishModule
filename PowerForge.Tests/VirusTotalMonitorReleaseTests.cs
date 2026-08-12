namespace PowerForge.Tests;

public sealed partial class VirusTotalMonitorReleaseTests
{
    [Fact]
    public void SelectArtifacts_ExplicitKinds_UsesOnlyKnownPackedReleaseOutputs()
    {
        var entries = new[]
        {
            Entry("TestimoX.1.2.3.zip", PowerForgeReleaseAssetCategory.Module, "modules/TestimoX.1.2.3.zip"),
            Entry("TestimoX.1.2.3.nupkg", PowerForgeReleaseAssetCategory.Package, "nuget/TestimoX.1.2.3.nupkg"),
            Entry("TestimoX.1.2.3.snupkg", PowerForgeReleaseAssetCategory.Package, "nuget/TestimoX.1.2.3.snupkg"),
            Entry("TestimoX-win-x64.zip", PowerForgeReleaseAssetCategory.Portable, "portable/TestimoX-win-x64.zip"),
            Entry("TestimoX.msi", PowerForgeReleaseAssetCategory.Installer, "installer/TestimoX.msi"),
            Entry("source.zip", PowerForgeReleaseAssetCategory.Other, "assets/source.zip"),
            Entry("release-manifest.json", PowerForgeReleaseAssetCategory.Metadata, "metadata/release-manifest.json")
        };

        var selected = VirusTotalReleaseArtifactSelector.Select(
            entries,
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[]
                {
                    VirusTotalArtifactKind.PowerShellModule,
                    VirusTotalArtifactKind.NuGetPackage,
                    VirusTotalArtifactKind.ZipArchive,
                    VirusTotalArtifactKind.MsiPackage
                }
            },
            "TestimoX",
            "1.2.3");

        Assert.Equal(4, selected.Length);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.PowerShellModule);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.NuGetPackage);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.ZipArchive);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.MsiPackage);
        Assert.DoesNotContain(selected, item => item.SourcePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(selected, item => item.SourcePath.EndsWith("source.zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfiguration_Disabled_DoesNotRequireCredentialsOrKinds()
    {
        VirusTotalReleaseArtifactSelector.ValidateConfiguration(new PowerForgeVirusTotalOptions());
    }

    [Fact]
    public void ValidateConfiguration_Enabled_RequiresOneCredentialSourceAndArtifactKind()
    {
        var options = new PowerForgeVirusTotalOptions { Enabled = true };

        var missing = Assert.Throws<InvalidOperationException>(
            () => VirusTotalReleaseArtifactSelector.ValidateConfiguration(options));
        Assert.Contains("API key", missing.Message, StringComparison.OrdinalIgnoreCase);

        options.ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY";
        var missingKinds = Assert.Throws<InvalidOperationException>(
            () => VirusTotalReleaseArtifactSelector.ValidateConfiguration(options));
        Assert.Contains("ArtifactKinds", missingKinds.Message, StringComparison.Ordinal);

        options.ArtifactKinds = new[] { VirusTotalArtifactKind.MsiPackage };
        options.ApiKeyFilePath = "secret.txt";
        var duplicateCredentials = Assert.Throws<InvalidOperationException>(
            () => VirusTotalReleaseArtifactSelector.ValidateConfiguration(options));
        Assert.Contains("exactly one", duplicateCredentials.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PlanOnly_DoesNotResolveVirusTotalSecretOrUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        File.WriteAllText(Path.Combine(root, "Build", "Build-Module.ps1"), "# plan-only module build");
        try
        {
            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ScriptPath = "Build/Build-Module.ps1"
                    },
                    VirusTotal = new PowerForgeVirusTotalOptions
                    {
                        Enabled = true,
                        ApiKeyEnvName = "POWERFORGE_TEST_MISSING_VIRUSTOTAL_KEY_" + Guid.NewGuid().ToString("N"),
                        ArtifactKinds = new[] { VirusTotalArtifactKind.PowerShellModule }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Null(result.VirusTotalMonitor);
            Assert.Null(result.VirusTotalMonitorReceiptPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectArtifacts_DefaultTemplate_PreservesUniqueRelativeArtifactPath()
    {
        var entry = Entry("App.zip", PowerForgeReleaseAssetCategory.Portable, "portable/win-x64/App.zip");
        entry.Version = null;
        var selected = VirusTotalReleaseArtifactSelector.Select(
            new[] { entry },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[] { VirusTotalArtifactKind.ZipArchive }
            },
            "Example",
            "2.0.0");

        var artifact = Assert.Single(selected);
        Assert.Equal("/Example/2.0.0/ZipArchive/portable/win-x64/App.zip", artifact.DestinationPath);
    }

    [Fact]
    public void SelectArtifacts_ExecutableAndMsixKinds_RequireExplicitSelection()
    {
        var selected = VirusTotalReleaseArtifactSelector.Select(
            new[]
            {
                Entry("App.exe", PowerForgeReleaseAssetCategory.Portable, "portable/App.exe"),
                Entry("App.msix", PowerForgeReleaseAssetCategory.Store, "store/App.msix"),
                Entry("App.zip", PowerForgeReleaseAssetCategory.Portable, "portable/App.zip")
            },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[]
                {
                    VirusTotalArtifactKind.Executable,
                    VirusTotalArtifactKind.MsixPackage
                }
            },
            "Example",
            "2.0.0");

        Assert.Equal(2, selected.Length);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.Executable);
        Assert.Contains(selected, item => item.Kind == VirusTotalArtifactKind.MsixPackage);
        Assert.DoesNotContain(selected, item => item.SourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectArtifacts_AppxUpload_IsAnExplicitMsixPackage()
    {
        var selected = VirusTotalReleaseArtifactSelector.Select(
            new[] { Entry("App.appxupload", PowerForgeReleaseAssetCategory.Store, "store/App.appxupload") },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[] { VirusTotalArtifactKind.MsixPackage }
            },
            "Example",
            "2.0.0");

        Assert.Equal(VirusTotalArtifactKind.MsixPackage, Assert.Single(selected).Kind);
    }

    [Fact]
    public void SelectArtifacts_UnverifiedConfiguredModuleZip_FailsClosed()
    {
        var sourceArchive = Entry(
            "Example-source.zip",
            PowerForgeReleaseAssetCategory.Module,
            "modules/Example-source.zip");
        sourceArchive.IsFinalPackageOutput = false;

        var exception = Assert.Throws<InvalidOperationException>(() => VirusTotalReleaseArtifactSelector.Select(
            new[] { sourceArchive },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[] { VirusTotalArtifactKind.PowerShellModule }
            },
            "Example",
            "2.0.0"));

        Assert.Contains("not a verified final package", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectArtifacts_DuplicateDestinationTemplate_FailsBeforeUpload()
    {
        var x64 = Entry("App.zip", PowerForgeReleaseAssetCategory.Portable, "portable/x64/App.zip");
        x64.Path = Path.Combine(Path.GetTempPath(), "x64", "App.zip");
        var arm64 = Entry("App.zip", PowerForgeReleaseAssetCategory.Portable, "portable/arm64/App.zip");
        arm64.Path = Path.Combine(Path.GetTempPath(), "arm64", "App.zip");
        var exception = Assert.Throws<InvalidOperationException>(() => VirusTotalReleaseArtifactSelector.Select(
            new[] { x64, arm64 },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = new[] { VirusTotalArtifactKind.ZipArchive },
                DestinationPathTemplate = "/{Project}/{Version}/{Kind}/{FileName}"
            },
            "Example",
            "2.0.0"));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publisher_UsesTypedClientAndReturnsHashVerifiedReceiptWithoutSecret()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "App.msi");
        await File.WriteAllTextAsync(artifactPath, "signed installer payload");
        string? capturedApiKey = null;
        try
        {
            var uploadedAt = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
            var publisher = new VirusTotalMonitorPublisher((apiKey, timeout) =>
            {
                capturedApiKey = apiKey;
                Assert.Equal(TimeSpan.FromMinutes(5), timeout);
                return new FakeClient();
            }, () => uploadedAt);

            var result = await publisher.PublishAsync(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = "top-secret-key",
                    RequestTimeout = TimeSpan.FromMinutes(5),
                    Artifacts = new[]
                    {
                        new VirusTotalMonitorArtifact
                        {
                            SourcePath = artifactPath,
                            Kind = VirusTotalArtifactKind.MsiPackage,
                            DestinationPath = "/Example/1.0.0/MsiPackage/App.msi",
                            Details = "Example 1.0.0 signed installer"
                        }
                    }
                },
                CancellationToken.None);

            Assert.Equal("top-secret-key", capturedApiKey);
            Assert.True(result.Success, result.ErrorMessage);
            var receipt = Assert.Single(result.Artifacts);
            Assert.Equal(VirusTotalMonitorVerificationStatus.Verified, receipt.VerificationStatus);
            Assert.Equal("LOCAL", receipt.LocalSha256);
            Assert.Equal("REMOTE", receipt.RemoteSha256);
            Assert.Equal(uploadedAt, receipt.UploadedAtUtc);
            Assert.DoesNotContain("top-secret-key", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Publisher_FailureAfterPartialUpload_CheckpointsReceiptAndSupportsExistingItemResume()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "First.msi");
        var secondPath = Path.Combine(root, "Second.msi");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var checkpoints = new List<VirusTotalMonitorPublishResult>();
        try
        {
            var client = new SequencedClient(failOnCall: 2);
            var publisher = new VirusTotalMonitorPublisher((_, _) => client);
            var failed = await publisher.PublishAsync(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = "secret",
                    Artifacts = new[]
                    {
                        Artifact(firstPath, "/Example/1.0.0/MsiPackage/First.msi"),
                        Artifact(secondPath, "/Example/1.0.0/MsiPackage/Second.msi")
                    },
                    CheckpointAsync = (checkpoint, _) =>
                    {
                        checkpoints.Add(checkpoint);
                        return Task.CompletedTask;
                    }
                });

            Assert.False(failed.Success);
            Assert.Contains("simulated", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            var firstReceipt = Assert.Single(failed.Artifacts);
            Assert.Equal("item-1", firstReceipt.MonitorId);
            Assert.Contains(checkpoints, checkpoint => checkpoint.Artifacts.Length == 1);

            var resumeClient = new SequencedClient();
            var resumedArtifact = Artifact(firstPath, firstReceipt.DestinationPath);
            resumedArtifact.ExistingItemId = firstReceipt.MonitorId;
            var resumed = await new VirusTotalMonitorPublisher((_, _) => resumeClient).PublishAsync(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = "secret",
                    Artifacts = new[] { resumedArtifact }
                });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal("item-1", Assert.Single(resumeClient.ExistingItemIds));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Publisher_CancellationAfterCompletedUpload_CheckpointsMonitorIdBeforeStopping()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "First.msi");
        var secondPath = Path.Combine(root, "Second.msi");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        using var cancellation = new CancellationTokenSource();
        var checkpoints = new List<VirusTotalMonitorPublishResult>();
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new CancelAfterUploadClient(cancellation));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = "secret",
                    Artifacts = new[]
                    {
                        Artifact(firstPath, "/Example/1.0.0/MsiPackage/First.msi"),
                        Artifact(secondPath, "/Example/1.0.0/MsiPackage/Second.msi")
                    },
                    CheckpointAsync = (checkpoint, checkpointToken) =>
                    {
                        Assert.False(checkpointToken.CanBeCanceled);
                        checkpoints.Add(checkpoint);
                        return Task.CompletedTask;
                    }
                },
                cancellation.Token));

            var checkpoint = Assert.Single(checkpoints);
            Assert.False(checkpoint.Success);
            Assert.Equal("uploaded-before-cancellation", Assert.Single(checkpoint.Artifacts).MonitorId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Publisher_FailureMessage_DoesNotPersistApiKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "App.msi");
        await File.WriteAllTextAsync(artifactPath, "installer");
        try
        {
            const string apiKey = "sensitive-api-key";
            var publisher = new VirusTotalMonitorPublisher((_, _) => new ThrowingSecretClient(apiKey));

            var result = await publisher.PublishAsync(new VirusTotalMonitorPublishRequest
            {
                ApiKey = apiKey,
                Artifacts = new[] { Artifact(artifactPath, "/Example/1.0.0/MsiPackage/App.msi") }
            });

            Assert.False(result.Success);
            Assert.DoesNotContain(apiKey, result.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PowerForgeReleaseAssetEntry Entry(
        string fileName,
        PowerForgeReleaseAssetCategory category,
        string relativeStagePath)
        => new()
        {
            Path = Path.Combine(Path.GetTempPath(), fileName),
            Category = category,
            RelativeStagePath = relativeStagePath,
            Version = "1.2.3",
            IsFinalPackageOutput = true
        };

}
