using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class AppStoreConnectScreenshotApprovalTests
{
    [Fact]
    public void Create_BindsReviewedCaptureFilesWithoutManualHashing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.ScreenshotApproval", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "01-home.png");
            File.WriteAllBytes(screenshotPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));

            var manifest = new AppStoreConnectScreenshotApprovalService().Create(
                new AppStoreConnectScreenshotApprovalRequest
                {
                    Spec = CreateSpec(),
                    BaseDirectory = root.FullName,
                    AllowedRoot = screenshotFolder.FullName,
                    VersionString = "1.5.0",
                    SourceCommit = "0123456789abcdef0123456789abcdef01234567",
                    ApprovedBy = "release-owner",
                    InitiatedBy = "workflow-initiator",
                    ApprovalEvidence = "https://github.example/actions/runs/123",
                    ApprovedAt = DateTimeOffset.Parse("2026-07-28T08:00:00Z"),
                    Runtime = "iOS 26.0",
                    Device = "iPhone 17 Pro Max",
                    Theme = "light",
                    Scenario = "store"
                });

            var entry = Assert.Single(manifest.Screenshots);
            Assert.Equal("screenshots/01-home.png", entry.File);
            Assert.Equal(ComputeSha256(screenshotPath), entry.Sha256);
            Assert.Equal(1, entry.Width);
            Assert.Equal(1, entry.Height);
            Assert.Equal("release-owner", manifest.ApprovedBy);
            Assert.Equal("workflow-initiator", manifest.InitiatedBy);
            Assert.Equal("https://github.example/actions/runs/123", manifest.ApprovalEvidence);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Create_RejectsScreenshotOutsideReviewedCaptureRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.ScreenshotApproval", Guid.NewGuid().ToString("N")));
        try
        {
            var reviewedFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "reviewed"));
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            File.WriteAllBytes(Path.Combine(screenshotFolder.FullName, "01-home.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppStoreConnectScreenshotApprovalService().Create(
                    new AppStoreConnectScreenshotApprovalRequest
                    {
                        Spec = CreateSpec(),
                        BaseDirectory = root.FullName,
                        AllowedRoot = reviewedFolder.FullName,
                        VersionString = "1.5.0",
                        SourceCommit = "0123456789abcdef0123456789abcdef01234567",
                        ApprovedBy = "release-owner"
                    }));

            Assert.Contains("outside the reviewed capture root", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Validate_RequiresExactApprovedScreenshotBytes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.ScreenshotApproval", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "01-home.png");
            File.WriteAllBytes(screenshotPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            var manifestPath = Path.Combine(root.FullName, "approval.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new AppStoreConnectScreenshotApprovalManifest
            {
                VersionString = "1.5.0",
                SourceCommit = "0123456789abcdef0123456789abcdef01234567",
                Locale = "en-US",
                Runtime = "iOS 26.0",
                Device = "iPhone 17 Pro Max",
                Theme = "light",
                Scenario = "home",
                ApprovedAt = DateTimeOffset.Parse("2026-07-28T08:00:00Z"),
                ApprovedBy = "release-owner",
                Screenshots = new[]
                {
                    new AppStoreConnectScreenshotApprovalEntry
                    {
                        ScreenshotDisplayType = "APP_IPHONE_67",
                        File = "screenshots/01-home.png",
                        Sha256 = ComputeSha256(screenshotPath),
                        Width = 1,
                        Height = 1
                    }
                }
            }));
            var spec = CreateSpec();

            var approved = new AppStoreConnectScreenshotSyncConfigValidator().Validate(spec, root.FullName);
            Assert.True(approved.IsValid);

            File.AppendAllText(screenshotPath, "changed-after-approval");
            var changed = new AppStoreConnectScreenshotSyncConfigValidator().Validate(spec, root.FullName);
            Assert.False(changed.IsValid);
            Assert.Contains(changed.Messages, message => message.Contains("changed after approval", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static AppStoreConnectScreenshotSyncSpec CreateSpec()
        => new()
        {
            AppId = "6778025328",
            VersionString = "1.5.0",
            Locale = "en-US",
            ScreenshotSets = new[]
            {
                new AppStoreConnectScreenshotSetSyncSpec
                {
                    ScreenshotDisplayType = "APP_IPHONE_67",
                    Path = "screenshots",
                    AllowedDimensions = new[] { "1x1" }
                }
            },
            Quality = new AppStoreConnectScreenshotQualitySpec
            {
                Enabled = true,
                MinimumFileBytes = 1,
                MinimumKilobytesPerMegapixel = 0,
                RequireApprovalManifest = true,
                ApprovalManifestPath = "approval.json"
            }
        };

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
