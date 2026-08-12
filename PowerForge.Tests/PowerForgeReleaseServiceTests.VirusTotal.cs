namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_MissingVirusTotalSecret_FailsBeforeAnyReleaseLaneRuns()
    {
        var root = CreateSandbox();
        var environmentName = $"POWERFORGE_TEST_VT_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ProjectName = "Example",
                ApiKeyEnvName = environmentName,
                ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
            };

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                }));

            Assert.Contains("did not produce a value", exception.Message, StringComparison.Ordinal);
            Assert.Empty(moduleCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublishBuiltReleaseOutputs_MultilineResolvedSecret_FailsBeforeRemoteUpload(bool useEnvironment)
    {
        var root = CreateSandbox();
        var environmentName = "POWERFORGE_TEST_VT_" + Guid.NewGuid().ToString("N");
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var secretPath = Path.Combine(root, "secret.txt");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            File.WriteAllText(secretPath, "first\nsecond");
            if (useEnvironment)
                Environment.SetEnvironmentVariable(environmentName, "first\nsecond");
            var uploadCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ApiKey = null;
            spec.VirusTotal.ApiKeyEnvName = useEnvironment ? environmentName : null;
            spec.VirusTotal.ApiKeyFilePath = useEnvironment ? null : secretPath;

            var exception = Assert.Throws<InvalidOperationException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains("single-line", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(uploadCalled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_VirusTotalFailure_IsReceiptedWithoutFailingPrimaryRelease()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "vt.json");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer placeholder");
            VirusTotalMonitorPublishRequest? capturedRequest = null;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (publishRequest, _) =>
                {
                    capturedRequest = publishRequest;
                    var failed = new VirusTotalMonitorPublishResult
                    {
                        Success = false,
                        ErrorMessage = "Monitor entitlement rejected the upload."
                    };
                    publishRequest.CheckpointAsync!(failed, CancellationToken.None).GetAwaiter().GetResult();
                    return failed;
                });
            var spec = new PowerForgeReleaseSpec
            {
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "Example",
                    ApiKey = "test-api-key",
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
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(capturedRequest);
            Assert.Equal("test-api-key", capturedRequest!.ApiKey);
            Assert.False(Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor).Success);
            Assert.Equal(receiptPath, result.VirusTotalMonitorReceiptPath);
            Assert.Contains("\"success\": false", File.ReadAllText(receiptPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_VirusTotalRunsAfterPrimaryGitHubRelease()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer placeholder");
            var calls = new List<string>();
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ =>
                {
                    calls.Add("GitHub");
                    return new GitHubReleasePublishResult { Succeeded = true };
                },
                publishVirusTotalMonitor: (_, _) =>
                {
                    calls.Add("VirusTotal");
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true,
                Owner = "EvotecIT",
                Repository = "Example",
                Token = "github-token"
            };
            var builtResult = CreateBuiltInstallerResult(artifactPath);

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                builtResult);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(new[] { "GitHub", "VirusTotal" }, calls);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ExistingReceipt_ResumesByMonitorItemId()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer placeholder");
            File.WriteAllText(receiptPath, """
                {
                  "schemaVersion": 1,
                  "provider": "VirusTotal Monitor",
                  "project": "Example",
                  "version": "1.2.3",
                  "success": false,
                  "artifacts": [
                    {
                      "destinationPath": "/Example/1.2.3/MsiPackage/Example.msi",
                      "monitorId": "existing-monitor-item"
                    }
                  ]
                }
                """);
            string? existingItemId = null;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (publishRequest, _) =>
                {
                    existingItemId = Assert.Single(publishRequest.Artifacts).ExistingItemId;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });

            var result = service.PublishBuiltReleaseOutputs(
                CreateVirusTotalInstallerSpec(),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("existing-monitor-item", existingItemId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_LockedReceipt_FailsBeforeRemoteUpload()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer placeholder");
            File.WriteAllText(receiptPath, "{}");
            var uploadCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;

            using var receiptLock = new FileStream(
                receiptPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(uploadCalled);
            Assert.False(Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor).Success);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ReceiptPathIsDirectory_FailsBeforeRemoteUpload()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "receipt.json");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            Directory.CreateDirectory(receiptPath);
            var uploadCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(uploadCalled);
            Assert.Contains(
                "existing directory",
                Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor).ErrorMessage,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_NoMatchingArtifactsAllowed_RecordsSkippedOutcome()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            var uploadCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ArtifactKinds = [VirusTotalArtifactKind.ZipArchive];
            spec.VirusTotal.RequireMatchingArtifacts = false;

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(uploadCalled);
            var monitor = Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor);
            Assert.True(monitor.Success, monitor.ErrorMessage);
            Assert.Empty(monitor.Artifacts);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_InvalidResumeReceipt_PreservesOriginalReceipt()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            var originalReceipt = "{ \"schemaVersion\": 999, \"provider\": \"Future Monitor\", \"monitorId\": \"recover-me\" }";
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "signed installer");
            File.WriteAllText(receiptPath, originalReceipt);
            var uploadCalled = false;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(uploadCalled);
            Assert.False(Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor).Success);
            Assert.Equal(originalReceipt, File.ReadAllText(receiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseSpec CreateVirusTotalInstallerSpec()
        => new()
        {
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ProjectName = "Example",
                ApiKey = "test-api-key",
                ArtifactKinds = [VirusTotalArtifactKind.MsiPackage]
            }
        };

    private static PowerForgeReleaseResult CreateBuiltInstallerResult(string artifactPath)
        => new()
        {
            Success = true,
            ReleaseAssets = [artifactPath],
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
}
