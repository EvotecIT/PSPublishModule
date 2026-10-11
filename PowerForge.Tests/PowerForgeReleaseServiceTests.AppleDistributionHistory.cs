using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("none", "success")]
    [InlineData("none", "nonzero")]
    [InlineData("none", "timeout")]
    [InlineData("none", "throw")]
    [InlineData("preserved-history", "success")]
    [InlineData("base-field", "success")]
    [InlineData("old-history", "success")]
    [InlineData("payload", "success")]
    [InlineData("transient-payload", "success")]
    [InlineData("info-alias", "success")]
    public void Execute_AppleUpload_bounds_distribution_history_without_granting_delivery_authority(string mutation, string outcome)
    {
        if (mutation == "info-alias" && OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        string? privateRoot = null;
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "synthetic fixture key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            var original = DistributionHistoryPlist();
            if (mutation is "old-history" or "preserved-history") AppendDistributionRecord(original, "original history");
            var originalBytes = original.ToString();
            var result = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: request =>
                {
                    var archive = CreateSuccessfulArchive(request);
                    File.WriteAllText(Path.Combine(request.ArchivePath!, "Info.plist"), originalBytes);
                    return archive;
                },
                uploadAppleApp: request =>
                {
                    request.InvokeRemoteMutationStarted();
                    privateRoot = Path.GetDirectoryName(request.ArchivePath);
                    var info = Path.Combine(request.ArchivePath!, "Info.plist");
                    var current = XDocument.Parse(File.ReadAllText(info));
                    AppendDistributionRecord(current, outcome);
                    if (mutation == "base-field") current.Descendants("string").First().Value = "unapproved base value";
                    if (mutation == "old-history") current.Descendants("string").Single(v => v.Value == "original history").Value = "rewritten history";
                    var staging = info + ".sb-2f65fadd-rg00hz";
                    File.WriteAllText(staging, current.ToString());
                    File.Move(staging, info, overwrite: true);
                    var payload = Path.Combine(request.ArchivePath!, "archive.bin");
                    if (mutation is "payload" or "transient-payload") File.WriteAllText(payload, "unapproved payload");
                    if (mutation == "transient-payload") File.WriteAllText(payload, "archive");
                    if (mutation == "info-alias") TestFileLink.CreateHardLink(Path.Combine(root, "info-alias"), info);
                    if (outcome == "throw") throw new IOException("response lost after remote handoff");
                    var upload = CreateSuccessfulUpload(request);
                    if (outcome != "success") upload.ProcessResult = new ProcessRunResult(
                        outcome == "timeout" ? -1 : 65, string.Empty, "indeterminate upload", "xcodebuild", TimeSpan.FromSeconds(1), outcome == "timeout");
                    return upload;
                }).Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = EnsureTestSourceCommit(root),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            var validHistory = mutation is "none" or "preserved-history";
            Assert.Equal(validHistory && outcome == "success", result.Success);
            var receipts = new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!);
            if (validHistory)
            {
                Assert.DoesNotContain(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed");
                Assert.Equal(outcome == "success", receipts.Any(receipt => receipt.OperationPhase == "UploadAttested"));
                if (outcome != "success") Assert.Contains(receipts, receipt => receipt.OperationPhase == "UploadAmbiguous");
            }
            else
            {
                Assert.Contains(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed");
                Assert.DoesNotContain(receipts, receipt => receipt.OperationPhase == "UploadAttested");
                Assert.True(Directory.Exists(privateRoot));
            }
            Assert.Equal(originalBytes, File.ReadAllText(Path.Combine(Assert.Single(result.AppleAppPlan!.Apps).ArchivePath, "Info.plist")));
        }
        finally
        {
            if (privateRoot is not null) TryDelete(privateRoot);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleArchiveUploadSnapshot_keeps_nonupload_and_late_plist_mutations_strict(bool enableUploadHistory)
    {
        var root = CreateSandbox();
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive")).FullName;
            var original = DistributionHistoryPlist();
            File.WriteAllText(Path.Combine(archive, "Info.plist"), original.ToString());
            var hash = AppleNotarizationService.ComputeArtifactSha256(archive);
            using var snapshot = AppleArchiveUploadSnapshot.Create(archive, hash, enableUploadHistory);
            var info = Path.Combine(snapshot.ArchivePath, "Info.plist");
            AppendDistributionRecord(original, "success");
            File.WriteAllText(info, original.ToString());
            if (!enableUploadHistory)
            {
                Assert.Throws<InvalidOperationException>(() => snapshot.ValidateUnchanged(hash));
                return;
            }
            snapshot.ValidateUnchanged(hash);
            var approvedBytes = File.ReadAllBytes(info);
            File.WriteAllText(info, "transient unapproved metadata");
            File.WriteAllBytes(info, approvedBytes);
            Assert.False(snapshot.IsExpectedXcodeExportMutation(new FileSystemEventArgs(WatcherChangeTypes.Changed, snapshot.ArchivePath, "Info.plist")));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void AppleArchiveUploadSnapshot_rejects_transient_base_plist_write_without_history_append()
    {
        var root = CreateSandbox();
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive")).FullName;
            var original = DistributionHistoryPlist().ToString();
            File.WriteAllText(Path.Combine(archive, "Info.plist"), original);
            var hash = AppleNotarizationService.ComputeArtifactSha256(archive);
            using var snapshot = AppleArchiveUploadSnapshot.Create(archive, hash, allowXcodeDistributionHistory: true);
            var info = Path.Combine(snapshot.ArchivePath, "Info.plist");
            File.WriteAllText(info, "transient unapproved metadata");
            File.WriteAllText(info, original);
            Assert.Throws<InvalidOperationException>(() => snapshot.ValidateUnchanged(hash));
        }
        finally { TryDelete(root); }
    }

    private static XDocument DistributionHistoryPlist() => XDocument.Parse("""
        <plist version="1.0"><dict>
        <key>ApplicationProperties</key><dict><key>CFBundleVersion</key><string>9</string></dict>
        <key>ArchiveVersion</key><integer>2</integer><key>Name</key><string>Fixture</string>
        </dict></plist>
        """);

    private static void AppendDistributionRecord(XDocument document, string state)
    {
        var dictionary = document.Root!.Element("dict")!;
        var key = dictionary.Elements("key").SingleOrDefault(value => value.Value == "Distributions");
        if (key is null)
        {
            dictionary.Add(new XElement("key", "Distributions"), new XElement("array"));
            key = dictionary.Elements("key").Last();
        }
        key.ElementsAfterSelf().First().Add(new XElement("dict", new XElement("key", "state"), new XElement("string", state)));
    }
}
