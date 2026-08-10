namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests {
    [Fact]
    public void Execute_AppleCheckpoint_rejects_transient_exact_source_snapshot_mutation() {
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var sourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive-only checkpoint must not query App Store Connect."),
                    archiveAppleApp: request => {
                        var projectFile = Path.Combine(request.ProjectPath, "project.pbxproj");
                        var original = File.ReadAllText(projectFile);
                        File.WriteAllText(projectFile, original + "\n// transient replacement");
                        File.WriteAllText(projectFile, original);
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "untrusted archive");
                        return CreateSuccessfulArchive(request);
                    })
                .Execute(CreateAppleAutomationSpec(root, keyPath), new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive,
                    AppleSourceCommit = sourceCommit,
                    RequireImmutableAppleSourceSnapshot = true
                });

            Assert.False(result.Success);
            Assert.Contains("snapshot changed while xcodebuild", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchive_builds_privately_and_publishes_exact_archive() {
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var planned = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive planning must not query App Store Connect."))
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive,
                    PlanOnly = true
                });
            var publicArchive = Assert.Single(planned.AppleAppPlan!.Apps).ArchivePath;
            string? privateArchive = null;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive-only execution must not query App Store Connect."),
                    archiveAppleApp: request => {
                        privateArchive = request.ArchivePath;
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "private exact archive");
                        Directory.CreateDirectory(publicArchive);
                        File.WriteAllText(Path.Combine(publicArchive, "payload"), "public replacement");
                        return CreateSuccessfulArchive(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(privateArchive);
            Assert.NotEqual(publicArchive, privateArchive);
            Assert.Equal("private exact archive", File.ReadAllText(Path.Combine(publicArchive, "payload")));
            Assert.False(Directory.Exists(Path.GetDirectoryName(privateArchive!)!));
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_persists_attestation_before_public_archive_recheck() {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            string? publicArchive = null;
            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request => {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "accepted bytes");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request => {
                        publicArchive = Directory.EnumerateDirectories(root, "*.xcarchive", SearchOption.AllDirectories).Single();
                        File.WriteAllText(Path.Combine(publicArchive, "payload"), "tampered after acceptance");
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.False(result.Success);
            Assert.NotNull(publicArchive);
            Assert.Contains("changed during upload", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested" &&
                           Assert.Single(receipt.Targets).UploadExecutionSha256 is not null);
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_rejects_transient_private_archive_snapshot_mutation() {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            string? privateArchive = null;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request => {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "approved bytes");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request => {
                        privateArchive = request.ArchivePath;
                        var payload = Path.Combine(request.ArchivePath, "payload");
                        File.WriteAllText(payload, "transient unapproved bytes");
                        File.WriteAllText(payload, "approved bytes");
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.False(result.Success);
            Assert.NotNull(privateArchive);
            Assert.Contains("private Apple upload archive snapshot changed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested");
        } finally {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("team")]
    [InlineData("signing")]
    [InlineData("xcode")]
    [InlineData("symbols")]
    [InlineData("archive-root")]
    [InlineData("export-root")]
    public void Execute_AppleUploadResume_rejects_changed_execution_policy(string changedControl) {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            var seeded = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request => {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "attested archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });
            Assert.True(seeded.Success, seeded.ErrorMessage);
            var seededTarget = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(seeded.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested").Targets.Single();
            Assert.Matches("^[0-9A-Fa-f]{64}$", seededTarget.UploadExecutionSha256!);

            switch (changedControl) {
                case "team": spec.AppleApps.TeamId = "DIFFERENTTEAM"; break;
                case "signing": spec.AppleApps.SigningStyle = "manual"; break;
                case "xcode": spec.AppleApps.XcodeBuildExecutable = "/reviewed/Xcode.app/xcodebuild"; break;
                case "symbols": spec.AppleApps.UploadSymbols = false; break;
                case "archive-root": spec.AppleApps.ArchiveRoot = "build/changed-archives"; break;
                case "export-root": spec.AppleApps.ExportRoot = "build/changed-exports"; break;
            }

            var resumed = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: "VALID"),
                    archiveAppleApp: _ => throw new InvalidOperationException("Changed policy must not reuse the prior upload."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Changed policy must not reuse the prior upload."))
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.False(resumed.Success);
            Assert.Contains("no immutable local upload receipt", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        } finally {
            TryDelete(root);
        }
    }

}
