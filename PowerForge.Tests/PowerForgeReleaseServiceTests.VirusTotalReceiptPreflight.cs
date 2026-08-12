namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
            Assert.Throws<IOException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.False(uploadCalled);
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
            var githubCalled = false;
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
                    uploadCalled = true;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = CreateVirusTotalInstallerSpec();
            spec.VirusTotal!.ReceiptPath = receiptPath;
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true,
                Owner = "EvotecIT",
                Repository = "Example",
                Token = "github-token"
            };

            var exception = Assert.Throws<InvalidOperationException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains("existing directory", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(githubCalled);
            Assert.False(uploadCalled);
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
            var originalReceipt =
                "{ \"schemaVersion\": 999, \"provider\": \"Future Monitor\", \"project\": \"Example\", \"version\": \"1.2.3\", \"monitorId\": \"recover-me\" }";
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

            var exception = Assert.Throws<InvalidDataException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains("unsupported schema", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(uploadCalled);
            Assert.Equal(originalReceipt, File.ReadAllText(receiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ReceiptWithoutExplicitIdentity_IsPreserved()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            const string originalReceipt = "{}";
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

            var exception = Assert.Throws<InvalidDataException>(() => service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest { ConfigPath = releasePath },
                CreateBuiltInstallerResult(artifactPath)));

            Assert.Contains("explicitly contain", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(uploadCalled);
            Assert.Equal(originalReceipt, File.ReadAllText(receiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ReceiptForDifferentProject_IsPreserved()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            const string originalReceipt = """
                {
                  "schemaVersion": 1,
                  "provider": "VirusTotal Monitor",
                  "project": "AnotherProject",
                  "version": "9.9.9",
                  "artifacts": []
                }
                """;
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

    [Fact]
    public void PublishBuiltReleaseOutputs_ReceiptForPreviousVersion_IsRotated()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
            const string originalReceipt = """
                {
                  "schemaVersion": 1,
                  "provider": "VirusTotal Monitor",
                  "project": "Example",
                  "version": "1.2.2",
                  "artifacts": []
                }
                """;
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
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                CreateBuiltInstallerResult(artifactPath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(uploadCalled);
            Assert.Equal(receiptPath, result.VirusTotalMonitorReceiptPath);
            Assert.Contains("\"version\": \"1.2.3\"", File.ReadAllText(receiptPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_SelectionFailure_PersistsFailureReceipt()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Example.msi");
            var receiptPath = Path.Combine(root, "Artifacts", "Release", "virustotal-monitor-receipt.json");
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
            spec.VirusTotal.ReceiptPath = receiptPath;

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
            Assert.Equal(receiptPath, result.VirusTotalMonitorReceiptPath);
            var monitor = Assert.IsType<VirusTotalMonitorPublishResult>(result.VirusTotalMonitor);
            Assert.False(monitor.Success);
            Assert.Contains("no release artifacts matched", monitor.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no release artifacts matched", File.ReadAllText(receiptPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
