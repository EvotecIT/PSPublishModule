namespace PowerForge.Tests;

public sealed class AppleReleaseLocalEvidenceTests
{
    [Fact]
    public void Evidence_AdmitsOnlyReceiptBoundArchiveFilesAndInactiveCanonicalLocks()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        var before = AppleNotarizationService.ComputeArtifactSha256(fixture.App.ArchivePath);
        var unrelated = Path.Combine(fixture.Root, "build", "unrelated.txt");
        File.WriteAllText(unrelated, "must remain ordinary dirt");

        var result = fixture.Validate();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(EvidenceFixture.SourceCommit, result.SourceCommit);
        Assert.Equal(fixture.ExpectedPaths(), result.EvidencePaths);
        Assert.DoesNotContain(unrelated, result.EvidencePaths);
        Assert.Equal(before, AppleNotarizationService.ComputeArtifactSha256(fixture.App.ArchivePath));
        Assert.All(fixture.LockPaths, path => Assert.Equal(0, new FileInfo(path).Length));
    }

    [Fact]
    public void Evidence_AdmitsReceiptsAndInactiveLocksWithoutAnArchive()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        Directory.Delete(fixture.App.ArchivePath, recursive: true);

        var result = fixture.Validate();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(fixture.ExpectedPaths(), result.EvidencePaths);
    }

    [Theory]
    [InlineData("archive-bytes")]
    [InlineData("receipt-path-escape")]
    [InlineData("source")]
    [InlineData("scheme")]
    [InlineData("plan-only")]
    public void Evidence_RejectsUnboundOrChangedArchive(string mutation)
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt(receipt =>
        {
            if (mutation == "receipt-path-escape") receipt.Targets[0].ArchivePath = "../escape.xcarchive";
            if (mutation == "source") receipt.SourceCommit = new string('b', 40);
            if (mutation == "scheme") receipt.Targets[0].Scheme = "Other";
            if (mutation == "plan-only") receipt.PlanOnly = true;
        });
        if (mutation == "archive-bytes") File.WriteAllText(fixture.ArchiveFile, "changed archive");

        var result = fixture.Validate();

        AssertRejected(result, AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation);
    }

    [Fact]
    public void Evidence_RejectsReparseArchiveEvenWhenItsBytesMatch()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        var original = fixture.App.ArchivePath + "-original";
        Directory.Move(fixture.App.ArchivePath, original);
        Directory.CreateSymbolicLink(fixture.App.ArchivePath, original);

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation);
    }

    [Fact]
    public void Evidence_RejectsAbsoluteArchiveLinkEvenWhenTargetIsInside()
    {
        using var fixture = new EvidenceFixture();
        File.CreateSymbolicLink(Path.Combine(fixture.App.ArchivePath, "alias"), fixture.ArchiveFile);
        fixture.WriteReceipt();

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation);
    }

    [Fact]
    public void Evidence_AdmitsVersionedFrameworkLinksAsExactTypedEvidence()
    {
        using var fixture = new EvidenceFixture();
        var framework = Path.Combine(fixture.App.ArchivePath, "Products", "Sample.app", "Contents", "Frameworks", "Sample.framework");
        var version = Path.Combine(framework, "Versions", "A");
        Directory.CreateDirectory(Path.Combine(version, "Resources"));
        File.WriteAllText(Path.Combine(version, "Sample"), "framework binary");
        Directory.CreateSymbolicLink(Path.Combine(framework, "Versions", "Current"), "A");
        Directory.CreateSymbolicLink(Path.Combine(framework, "Resources"), "Versions/Current/Resources");
        File.CreateSymbolicLink(Path.Combine(framework, "Sample"), "Versions/Current/Sample");
        fixture.WriteReceipt();

        var result = fixture.Validate();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, result.ArchiveLinks.Length);
        Assert.All(result.ArchiveLinks, link => Assert.Equal(fixture.App.ArchivePath, link.ArchiveRoot));
        var binaryLink = Assert.Single(result.ArchiveLinks, link => link.Path == Path.Combine(framework, "Sample"));
        Assert.Equal("Versions/Current/Sample", binaryLink.LinkTarget);
        Assert.Equal(Path.Combine(version, "Sample"), binaryLink.ResolvedTargetPath);
        Assert.False(binaryLink.IsDirectory);
        Assert.Contains(Path.Combine(version, "Sample"), result.EvidencePaths);
        Assert.DoesNotContain(binaryLink.Path, result.EvidencePaths);
        var copy = Path.Combine(fixture.Root, "framework-copy.xcarchive");
        AppleArtifactCopy.CopyDirectory(fixture.App.ArchivePath, copy);
        var copiedBinaryLink = Path.Combine(copy, FrameworkCompatibility.GetRelativePath(fixture.App.ArchivePath, binaryLink.Path));
        Assert.Equal(binaryLink.LinkTarget, new FileInfo(copiedBinaryLink).LinkTarget);
        Assert.Equal("framework binary", File.ReadAllText(copiedBinaryLink));
    }

    [Fact]
    public void Evidence_RejectsIntermediateLinkThatEscapesDespiteLexicallyContainedTarget()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        var folder = Path.Combine(fixture.App.ArchivePath, "folder");
        Directory.CreateDirectory(folder);
        Directory.CreateSymbolicLink(Path.Combine(folder, "Parent"), "..");
        File.CreateSymbolicLink(Path.Combine(folder, "Escape"), "Parent/../release.json");

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation);
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("cycle")]
    public void Evidence_RejectsUnsafeRelativeArchiveLink(string kind)
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        var link = Path.Combine(fixture.App.ArchivePath, "link");
        File.CreateSymbolicLink(link, kind == "escape" ? "../../release.json" : "link");

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation);
    }

    [Fact]
    public void Evidence_RejectsActiveJournalLock()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        using var lease = AppleReleaseReceiptJournalLease.Acquire(fixture.Plan);

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.JournalLockValidation);
    }

    [Fact]
    public void Evidence_RejectsNonemptyCanonicalLockWithoutChangingIt()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        File.WriteAllText(fixture.LockPaths[0], "unexpected state");

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.JournalLockValidation);
        Assert.Equal("unexpected state", File.ReadAllText(fixture.LockPaths[0]));
    }

    [Fact]
    public void Evidence_RejectsBrokenReceiptChainBeforeAdmittingFiles()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        var history = Assert.Single(Directory.GetFiles(fixture.Plan.ReceiptHistoryPath));
        File.WriteAllText(history, File.ReadAllText(history).Replace("Sample", "Changed", StringComparison.Ordinal));

        AssertRejected(fixture.Validate(), AppleReleaseLocalEvidenceRejectionPhase.ReceiptValidation);
    }

    [Fact]
    public void Evidence_AdmitsUnchangedOriginalArchiveAfterPrivateUploadIntegrityFailure()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        fixture.WriteReceipt(receipt =>
        {
            receipt.OperationPhase = "UploadIntegrityFailed";
            receipt.Success = false;
        });

        var result = fixture.Validate();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(fixture.ExpectedPaths(), result.EvidencePaths);
    }

    [Fact]
    public void Evidence_DoesNotCreateMissingReceiptOrLockState()
    {
        using var fixture = new EvidenceFixture();
        Directory.Delete(fixture.App.ArchivePath, recursive: true);
        var before = Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories);

        var result = fixture.Validate();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.EvidencePaths);
        Assert.Empty(result.ArchiveLinks);
        Assert.Equal(before, Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("divergent-source")]
    [InlineData("old-path")]
    [InlineData("different-bytes")]
    public void Evidence_NewerUnrelatedReceiptCannotMaskAnExactValidArtifact(string invalid)
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteReceipt();
        fixture.WriteReceipt(receipt =>
        {
            if (invalid == "divergent-source") receipt.SourceCommit = new string('b', 40);
            if (invalid == "old-path") receipt.Targets[0].ArchivePath = "build/old.xcarchive";
            if (invalid == "different-bytes") receipt.Targets[0].ArchiveSha256 = new string('b', 64);
        });
        var result = fixture.Validate();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains(fixture.ArchiveFile, result.EvidencePaths);
    }

    [Fact]
    public void Evidence_ValidAncestorSurvivesARecentDivergentReceipt()
    {
        using var fixture = new EvidenceFixture();
        void Git(params string[] arguments)
        {
            using var process = System.Diagnostics.Process.Start(AppleTestFlightWorker.CreateProcess("git", fixture.Root,
                new[] { "-c", "core.hooksPath=/dev/null", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid" }.Concat(arguments)))!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        Git("init", "--quiet");
        File.WriteAllText(Path.Combine(fixture.Root, ".gitignore"), "build/\n");
        File.WriteAllText(Path.Combine(fixture.Root, "source"), "original");
        Git("add", "."); Git("commit", "--quiet", "-m", "original");
        string Head()
        {
            using var process = System.Diagnostics.Process.Start(AppleTestFlightWorker.CreateProcess("git", fixture.Root, ["rev-parse", "HEAD"]))!;
            var value = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); Assert.Equal(0, process.ExitCode); return value;
        }
        var ancestor = Head();
        fixture.WriteReceipt(receipt => receipt.SourceCommit = ancestor);
        File.WriteAllText(Path.Combine(fixture.Root, "source"), "new source");
        Git("add", "."); Git("commit", "--quiet", "-m", "new source");
        fixture.Plan.SourceCommit = Head();
        fixture.WriteReceipt(receipt => receipt.SourceCommit = new string('b', 40));
        var result = fixture.Validate();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains(fixture.ArchiveFile, result.EvidencePaths);
    }

    private static void AssertRejected(AppleReleaseLocalEvidence result, AppleReleaseLocalEvidenceRejectionPhase phase)
    {
        Assert.False(result.Success);
        Assert.Equal(phase, result.RejectionPhase);
        Assert.NotEmpty(result.ErrorMessage!);
        Assert.Empty(result.EvidencePaths);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        internal const string SourceCommit = "0123456789abcdef0123456789abcdef01234567";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PowerForge-LocalEvidence", Guid.NewGuid().ToString("N"));
        internal PowerForgeAppleReleasePlan Plan { get; }
        internal PowerForgeAppleAppReleaseTargetPlan App => Plan.Apps[0];
        internal string ArchiveFile => Path.Combine(App.ArchivePath, "Products", "Sample.app", "Sample");
        internal string[] LockPaths => new[]
        {
            AppleReleaseReceiptJournalLease.CreateLockPath(Plan.ReceiptPath),
            AppleReleaseReceiptJournalLease.CreateLockPath(Plan.ReceiptHistoryPath)
        };

        internal EvidenceFixture()
        {
            Plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = Root,
                SourceCommit = SourceCommit,
                ReceiptPath = Path.Combine(Root, "build", "release.json"),
                ReceiptHistoryPath = Path.Combine(Root, "build", "history"),
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        Name = "Sample", BundleId = "dev.sample.app", Platform = ApplePlatform.iOS,
                        ProjectPath = Path.Combine(Root, "Sample.xcodeproj"), Scheme = "Sample",
                        Configuration = "Release", Destination = "generic/platform=iOS",
                        ArchivePath = Path.Combine(Root, "build", "archives", "iOS", "Sample.xcarchive")
                    }
                }
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ArchiveFile)!);
            File.WriteAllText(ArchiveFile, "approved archive");
        }

        internal void WriteReceipt(Action<PowerForgeAppleReleaseReceipt>? configure = null)
        {
            var receipt = new PowerForgeAppleReleaseReceipt
            {
                SourceCommit = SourceCommit, Action = PowerForgeAppleReleaseAction.Archive,
                OperationPhase = "Completed", Success = true,
                Targets = new[]
                {
                    new PowerForgeAppleReleaseTargetReceipt
                    {
                        Name = App.Name, BundleId = App.BundleId, Platform = App.Platform,
                        Configuration = App.Configuration, ProjectPath = "Sample.xcodeproj", Scheme = App.Scheme,
                        ArchiveVariant = App.ArchiveVariant, DistributionRoute = App.DistributionRoute,
                        Destination = App.Destination, ArchiveCreated = true,
                        ArchivePath = FrameworkCompatibility.GetRelativePath(Root, App.ArchivePath).Replace('\\', '/'),
                        ArchiveSha256 = AppleNotarizationService.ComputeArtifactSha256(App.ArchivePath)
                    }
                }
            };
            configure?.Invoke(receipt);
            new AppleReleaseReceiptStore().WriteAttempt(Plan, receipt);
        }

        internal AppleReleaseLocalEvidence Validate()
            => new PowerForgeReleaseService(new NullLogger()).ValidateLocalAppleEvidence(Plan);

        internal string[] ExpectedPaths()
            => Directory.GetFiles(Plan.ReceiptHistoryPath).Append(Plan.ReceiptPath).Concat(LockPaths)
                .Concat(File.Exists(ArchiveFile) ? new[] { ArchiveFile } : Array.Empty<string>())
                .OrderBy(static path => path, StringComparer.Ordinal).ToArray();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
