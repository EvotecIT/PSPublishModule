using System.Net;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReleasePreparationService_RejectsScreenshotInventoryDriftBeforeFirstMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "01-home.png");
            await File.WriteAllBytesAsync(screenshotPath, new byte[] { 1, 2, 3 });
            var approvedInventorySha256 = AppStoreConnectScreenshotInventory.ComputeSha256(
            [
                new AppStoreConnectReleaseScreenshotSetReadiness
                {
                    ScreenshotDisplayType = "APP_IPHONE_65",
                    ScreenshotSetId = "set-1",
                    Count = 1,
                    Screenshots =
                    [
                        new AppStoreConnectReleaseScreenshotAssetReadiness
                        {
                            Id = "shot-before",
                            FileName = "01-home.png",
                            FileSize = 3,
                            SourceFileChecksum = "approved-checksum",
                            AssetDeliveryState = "COMPLETE"
                        }
                    ]
                }
            ]);
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "build-5", "type": "builds", "attributes": { "version": "5", "processingState": "VALID", "expired": false }, "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } } }], "included": [{ "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": null }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "shot-after", "type": "appScreenshots", "attributes": { "fileName": "01-home.png", "fileSize": 3, "sourceFileChecksum": "changed-checksum", "assetDeliveryState": { "state": "COMPLETE" } } }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppStoreConnectReleasePreparationService(client).PrepareAsync(new AppStoreConnectReleasePreparationRequest
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    BuildNumber = "5",
                    Platform = ApplePlatform.iOS,
                    CreateVersion = false,
                    SelectBuild = true,
                    ReplaceScreenshots = true,
                    BaseDirectory = root.FullName,
                    ExpectedScreenshotFileSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [screenshotPath] = ComputeScreenshotSha256(screenshotPath)
                    },
                    ExpectedScreenshotInventorySha256 = approvedInventorySha256,
                    ScreenshotSpec = new AppStoreConnectScreenshotSyncSpec
                    {
                        AppId = "app-1",
                        VersionString = "1.0.0",
                        VersionId = "version-1",
                        Platform = ApplePlatform.iOS,
                        Locale = "en-US",
                        ScreenshotSets =
                        [
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPHONE_65",
                                Path = "iphone-6-5"
                            }
                        ]
                    }
                }));

            Assert.Contains("before any remote release mutation", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(5, handler.RequestUris.Count);
            Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ReleasePreparationService_ReusesApprovedScreenshotSnapshotAfterEarlierRemoteMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "01-home.png");
            var approvedBytes = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(screenshotPath, approvedBytes);
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "build-5", "type": "builds", "attributes": { "version": "5", "processingState": "VALID", "expired": false }, "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } } }], "included": [{ "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": null }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
                ScreenshotReservation("shot-1", "01-home.png", approvedBytes.Length),
                ScreenshotCommit("shot-1", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "shot-1", "type": "appScreenshots", "attributes": { "fileName": "01-home.png", "fileSize": 3, "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac", "assetDeliveryState": { "state": "UPLOAD_COMPLETE" } } }] }"""));
            handler.OnRequest = count =>
            {
                if (count == 1)
                    File.WriteAllBytes(screenshotPath, new byte[] { 9, 9, 9 });
            };
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(
                new AppStoreConnectReleasePreparationRequest
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    BuildNumber = "5",
                    Platform = ApplePlatform.iOS,
                    CreateVersion = false,
                    SelectBuild = true,
                    ReplaceScreenshots = true,
                    BaseDirectory = root.FullName,
                    ExpectedScreenshotFileSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [screenshotPath] = ComputeScreenshotSha256(screenshotPath)
                    },
                    ScreenshotSpec = new AppStoreConnectScreenshotSyncSpec
                    {
                        AppId = "app-1",
                        VersionString = "1.0.0",
                        VersionId = "version-1",
                        Platform = ApplePlatform.iOS,
                        Locale = "en-US",
                        ScreenshotSets =
                        [
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPHONE_65",
                                Path = "iphone-6-5"
                            }
                        ]
                    }
                });

            Assert.True(result.SelectedBuild);
            Assert.Equal(new HttpMethod("PATCH"), handler.Methods[2]);
            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(screenshotPath));
            Assert.Equal(screenshotPath, Assert.Single(Assert.Single(result.Screenshots!.ScreenshotSets).Uploaded).FilePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_PreservesSubdirectoryFiltersInApprovedInventory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var nested = Directory.CreateDirectory(Path.Combine(folder.FullName, "iPhone"));
            var screenshotPath = Path.Combine(nested.FullName, "01-home.png");
            await File.WriteAllBytesAsync(screenshotPath, new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
                new SequenceResponse(HttpStatusCode.Created, """{ "data": { "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } } }"""),
                ScreenshotReservation("shot-1", "01-home.png", 3),
                ScreenshotCommit("shot-1", "01-home.png", "5289df737df57326fcdd22597afb1fac"));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var result = await new AppStoreConnectScreenshotSyncService(client).SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                ExpectedFileSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [screenshotPath] = ComputeScreenshotSha256(screenshotPath)
                },
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
                            Path = "screenshots",
                            Filter = "iPhone/*.png"
                        }
                    ]
                }
            });

            Assert.Equal(screenshotPath, Assert.Single(Assert.Single(result.ScreenshotSets).Uploaded).FilePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_PreservesRelativePathsForDuplicateNestedBasenames()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "screenshots"));
            var phoneFolder = Directory.CreateDirectory(Path.Combine(folder.FullName, "iPhone"));
            var tabletFolder = Directory.CreateDirectory(Path.Combine(folder.FullName, "iPad"));
            var phone = Path.Combine(phoneFolder.FullName, "shot.png");
            var tablet = Path.Combine(tabletFolder.FullName, "shot.png");
            await File.WriteAllBytesAsync(phone, new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(tablet, new byte[] { 4, 5, 6 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
                new SequenceResponse(HttpStatusCode.Created, """{ "data": { "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } } }"""),
                ScreenshotReservation("shot-phone", "shot.png", 3),
                ScreenshotCommit("shot-phone", "shot.png", "5289df737df57326fcdd22597afb1fac"),
                ScreenshotReservation("shot-tablet", "shot.png", 3),
                ScreenshotCommit("shot-tablet", "shot.png", "b4a3ba90641372b4e4eaa841a5a400ec"));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var result = await new AppStoreConnectScreenshotSyncService(client).SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                ExpectedFileSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [phone] = ComputeScreenshotSha256(phone),
                    [tablet] = ComputeScreenshotSha256(tablet)
                },
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
                            Path = "screenshots",
                            Filter = "*/shot.png"
                        }
                    ]
                }
            });

            var uploaded = Assert.Single(result.ScreenshotSets).Uploaded;
            Assert.Equal(2, uploaded.Length);
            Assert.Equal(new[] { tablet, phone }, uploaded.Select(static item => item.FilePath).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ScreenshotUploadSnapshot_UsesPrivateFileBackedRanges()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotPath = Path.Combine(root.FullName, "large.png");
            var bytes = Enumerable.Range(0, 1024 * 1024).Select(static value => (byte)(value % 251)).ToArray();
            await File.WriteAllBytesAsync(screenshotPath, bytes);
            using var snapshot = AppStoreConnectScreenshotUploadSnapshot.Capture(
                screenshotPath,
                ComputeScreenshotSha256(screenshotPath));
            File.WriteAllBytes(screenshotPath, new byte[] { 9, 9, 9 });
            using var content = snapshot.CreateRangeContent(700_000, 7);

            var range = await content.ReadAsByteArrayAsync();

            Assert.Equal(bytes.Skip(700_000).Take(7), range);
            Assert.NotEqual(Path.GetFullPath(screenshotPath), snapshot.FilePath);
            Assert.Equal(bytes.LongLength, snapshot.Length);
            Assert.Equal(7, content.Headers.ContentLength);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(snapshot.RootPath));
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UploadScreenshotAsync_RejectsPrivateSnapshotMutationDuringReservation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotPath = Path.Combine(root.FullName, "01-home.png");
            await File.WriteAllBytesAsync(screenshotPath, new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(ScreenshotReservation("shot-1", "01-home.png", 3));
            handler.OnRequest = count =>
            {
                if (count != 1)
                    return;
                var snapshotBase = Path.Combine(Path.GetTempPath(), "PowerForge", "appstore-screenshot-upload");
                var snapshot = Directory.EnumerateFiles(snapshotBase, "screenshot-bytes", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .First();
                File.WriteAllBytes(snapshot, new byte[] { 9, 9, 9 });
            };
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.UploadScreenshotAsync("set-1", screenshotPath));

            Assert.Contains("private screenshot upload snapshot changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(handler.RequestUris);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static string ComputeScreenshotSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
