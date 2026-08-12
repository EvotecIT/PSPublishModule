using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public void ScreenshotSyncService_binds_direct_approval_hashes_into_the_immutable_snapshot()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var sourcePath = Path.Combine(folder.FullName, "01-home.png");
            File.WriteAllBytes(sourcePath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            var spec = new AppStoreConnectScreenshotSyncSpec
            {
                AppId = "app-1",
                VersionString = "1.0.0",
                Platform = ApplePlatform.iOS,
                Locale = "en-US",
                ScreenshotSets =
                [
                    new AppStoreConnectScreenshotSetSyncSpec
                    {
                        ScreenshotDisplayType = "APP_IPHONE_65",
                        Path = "screenshots"
                    }
                ],
                Quality = new AppStoreConnectScreenshotQualitySpec
                {
                    RequireApprovalManifest = true,
                    ApprovalManifestPath = "approval.json"
                }
            };
            var manifest = new AppStoreConnectScreenshotApprovalService().Create(
                new AppStoreConnectScreenshotApprovalRequest
                {
                    Spec = spec,
                    BaseDirectory = root.FullName,
                    AllowedRoot = folder.FullName,
                    VersionString = spec.VersionString!,
                    SourceCommit = sourceCommit,
                    ApprovedBy = "release-owner"
                });
            File.WriteAllText(
                Path.Combine(root.FullName, "approval.json"),
                JsonSerializer.Serialize(manifest));
            var validation = new AppStoreConnectScreenshotSyncConfigValidator().Validate(
                spec,
                root.FullName,
                expectedSourceCommit: sourceCommit);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Messages));

            File.WriteAllBytes(sourcePath, new byte[] { 9, 9, 9 });
            var handler = new SequenceHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);
            var request = new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                ExpectedSourceCommit = sourceCommit,
                Spec = spec
            };

            var exception = Assert.Throws<InvalidOperationException>(() => service.CreateSnapshot(request, validation));

            Assert.Contains("changed after Apple plan approval", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_rejects_restored_bytes_changed_through_snapshot_hard_link_before_remote_selection()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? aliasRoot = null;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var sourcePath = Path.Combine(folder.FullName, "01-home.png");
            File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);
            using var snapshot = service.CreateSnapshot(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                Spec = new AppStoreConnectScreenshotSyncSpec
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    Platform = ApplePlatform.iOS,
                    Locale = "en-US",
                    ScreenshotSets =
                    [
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            Path = "screenshots"
                        }
                    ]
                }
            });
            var snapshotPath = Assert.Single(Assert.Single(snapshot.Sets).Files);
            aliasRoot = Path.Combine(Directory.GetParent(Path.GetDirectoryName(snapshotPath)!)!.FullName, $"alias-{Guid.NewGuid():N}");
            Directory.CreateDirectory(aliasRoot);
            var aliasPath = Path.Combine(aliasRoot, "screenshot-alias");
            TestFileLink.CreateHardLink(aliasPath, snapshotPath);
            File.WriteAllBytes(aliasPath, new byte[] { 9, 9, 9 });
            File.WriteAllBytes(aliasPath, new byte[] { 1, 2, 3 });
            File.Delete(aliasPath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(
                new AppStoreConnectScreenshotSyncRequest
                {
                    BaseDirectory = root.FullName,
                    Spec = new AppStoreConnectScreenshotSyncSpec
                    {
                        AppId = "app-1",
                        VersionString = "1.0.0",
                        Platform = ApplePlatform.iOS,
                        Locale = "en-US",
                        ScreenshotSets =
                        [
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPHONE_65",
                                Path = "screenshots"
                            }
                        ]
                    }
                },
                snapshot));

            Assert.Contains("screenshot snapshot", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(aliasRoot) && Directory.Exists(aliasRoot))
                Directory.Delete(aliasRoot, recursive: true);
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
