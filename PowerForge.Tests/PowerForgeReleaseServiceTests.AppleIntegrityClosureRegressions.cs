namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests {
    [Fact]
    public void Execute_AppleCheckpoint_rejects_linked_swiftpm_metadata_root() {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateSandbox();
        var outside = root + "-swiftpm-metadata";
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            File.WriteAllText(
                Path.Combine(root, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"CasaRay\")\n");
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(root, ".swiftpm"), outside);
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
                    archiveAppleApp: _ => throw new InvalidOperationException("Linked SwiftPM metadata must fail before archive."))
                .Execute(CreateAppleAutomationSpec(root, keyPath), new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive,
                    AppleSourceCommit = sourceCommit,
                    RequireImmutableAppleSourceSnapshot = true
                });

            Assert.False(result.Success);
            Assert.Contains("symbolic link", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        } finally {
            TryDelete(root);
            TryDelete(outside);
        }
    }

    [Fact]
    public void AppleReleaseSourceSnapshot_revalidates_prepared_swiftpm_state_when_monitoring_begins() {
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            File.WriteAllText(
                Path.Combine(root, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"CasaRay\")\n");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var sourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();

            using var snapshot = AppleReleaseSourceSnapshot.CreateIfRequired(new PowerForgeAppleReleasePlan {
                ProjectRoot = root,
                SourceCommit = sourceCommit,
                RequireImmutableSourceSnapshot = true,
                Archive = true
            });
            Assert.NotNull(snapshot);
            File.WriteAllText(
                Path.Combine(snapshot!.RootPath, ".swiftpm", "configuration", "workspace-state.json"),
                "unbound state inserted after snapshot preflight");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.MonitorChanges());

            Assert.Contains("untracked state", exception.Message, StringComparison.OrdinalIgnoreCase);
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleCheckpoint_rejects_files_created_in_prepared_swiftpm_directories() {
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            File.WriteAllText(
                Path.Combine(root, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"CasaRay\")\n");
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
                        var snapshotRoot = Path.GetDirectoryName(request.ProjectPath)!;
                        File.WriteAllText(
                            Path.Combine(snapshotRoot, ".swiftpm", "configuration", "workspace-state.json"),
                            "untrusted state");
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
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested" &&
                           Assert.Single(receipt.Targets).UploadPerformed);
        } finally {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("rg00hz")]
    [InlineData("3qvlpX")]
    public void Execute_AppleUpload_allows_transient_xcode_sandbox_scratch_for_approved_archive_file(string finalToken) {
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

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request => {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "Info.plist"), "approved bytes");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request => {
                        var scratch = Path.Combine(
                            request.ArchivePath!,
                            $"Info.plist.sb-2f65fadd-{finalToken}");
                        File.WriteAllText(scratch, "xcode scratch bytes");
                        Thread.Sleep(1000);
                        File.Delete(scratch);
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(Assert.Single(result.AppleApps).Upload?.Succeeded);
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePreparePlan_reuses_remote_screenshots_without_local_approval_or_pixels() {
        var root = CreateSandbox();
        try {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(
                Path.Combine(root, "screenshots.json"),
                """
                {
                  "appId": "6778025328",
                  "versionString": "1.2.0",
                  "platform": "iOS",
                  "locale": "en-US",
                  "quality": {
                    "enabled": true,
                    "requireApprovalManifest": true,
                    "approvalManifestPath": "missing.approval.json"
                  },
                  "screenshotSets": [
                    {
                      "screenshotDisplayType": "APP_IPHONE_67",
                      "path": "missing-screenshots",
                      "filter": "*.png"
                    }
                  ]
                }
                """);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.SyncScreenshots = true;
            spec.AppleApps.ReplaceScreenshots = true;
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request))
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Prepare,
                    PlanOnly = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan).SyncScreenshots);
            Assert.Contains("screenshots.json", result.AppleReceipt!.MutationInputFiles.Keys);
            Assert.DoesNotContain("missing.approval.json", result.AppleReceipt.MutationInputFiles.Keys);
        } finally {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("Unapproved.plist.sb-2f65fadd-rg00hz")]
    [InlineData("Products/Info.plist.sb-2f65fadd-rg00hz")]
    public void Execute_AppleUpload_rejects_xcode_sandbox_scratch_outside_exact_archive_info_plist(string scratchRelativePath) {
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

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request => {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "Info.plist"), "approved bytes");
                        var products = Directory.CreateDirectory(Path.Combine(archive.FullName, "Products"));
                        File.WriteAllText(Path.Combine(products.FullName, "Info.plist"), "approved nested bytes");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request => {
                        var scratch = Path.Combine(request.ArchivePath!, scratchRelativePath);
                        File.WriteAllText(scratch, "unapproved scratch bytes");
                        Thread.Sleep(1000);
                        File.Delete(scratch);
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.False(result.Success);
            Assert.Contains("private Apple upload archive snapshot changed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        } finally {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_rejects_transient_private_archive_hard_link_alias_mutation() {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        string? aliasRoot = null;
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
                        var snapshotRoot = Directory.GetParent(request.ArchivePath)!.FullName;
                        aliasRoot = Path.Combine(Directory.GetParent(snapshotRoot)!.FullName, $"alias-{Guid.NewGuid():N}");
                        Directory.CreateDirectory(aliasRoot);
                        var alias = Path.Combine(aliasRoot, "payload-alias");
                        TestFileLink.CreateHardLink(alias, payload);
                        File.WriteAllText(alias, "transient unapproved bytes");
                        File.WriteAllText(alias, "approved bytes");
                        File.Delete(alias);
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.False(result.Success);
            Assert.NotNull(privateArchive);
            Assert.Contains("private Apple upload archive snapshot", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested" &&
                           Assert.Single(receipt.Targets).UploadPerformed);
        } finally {
            if (!string.IsNullOrWhiteSpace(aliasRoot) && Directory.Exists(aliasRoot))
                Directory.Delete(aliasRoot, recursive: true);
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleArchiveUploadSnapshot_rejects_restored_bytes_changed_through_a_removed_hard_link_alias() {
        var root = CreateSandbox();
        string? aliasRoot = null;
        try {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive"));
            File.WriteAllText(Path.Combine(archive.FullName, "payload"), "approved bytes");
            var expectedSha256 = AppleNotarizationService.ComputeArtifactSha256(archive.FullName);

            using var snapshot = AppleArchiveUploadSnapshot.Create(archive.FullName, expectedSha256);
            aliasRoot = Path.Combine(Directory.GetParent(snapshot.RootPath)!.FullName, $"alias-{Guid.NewGuid():N}");
            Directory.CreateDirectory(aliasRoot);
            var alias = Path.Combine(aliasRoot, "payload-alias");
            TestFileLink.CreateHardLink(alias, Path.Combine(snapshot.ArchivePath, "payload"));
            File.WriteAllText(alias, "transient unapproved bytes");
            File.WriteAllText(alias, "approved bytes");
            File.Delete(alias);

            var exception = Assert.Throws<InvalidOperationException>(
                () => snapshot.ValidateUnchanged(expectedSha256));
            Assert.Contains("hard-link alias", exception.Message, StringComparison.OrdinalIgnoreCase);
        } finally {
            if (!string.IsNullOrWhiteSpace(aliasRoot) && Directory.Exists(aliasRoot))
                Directory.Delete(aliasRoot, recursive: true);
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleArchiveUploadSnapshot_disposes_read_only_nested_directories_without_failing() {
        var root = CreateSandbox();
        try {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive"));
            var nested = Directory.CreateDirectory(Path.Combine(archive.FullName, "Products", "ReadOnly.app"));
            File.WriteAllText(Path.Combine(nested.FullName, "payload"), "approved bytes");
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(nested.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
#endif
            var expectedSha256 = AppleNotarizationService.ComputeArtifactSha256(archive.FullName);
            var snapshot = AppleArchiveUploadSnapshot.Create(archive.FullName, expectedSha256);
            var snapshotRoot = snapshot.RootPath;

            var exception = Record.Exception(snapshot.Dispose);

            Assert.Null(exception);
            Assert.False(Directory.Exists(snapshotRoot));
        } finally {
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows() && Directory.Exists(root))
                File.SetUnixFileMode(Path.Combine(root, "approved.xcarchive", "Products", "ReadOnly.app"),
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
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
