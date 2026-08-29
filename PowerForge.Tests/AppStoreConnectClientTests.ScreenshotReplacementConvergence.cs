using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingWaitsForEventualInventoryConvergence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": {} }, { "id": "approved", "type": "appScreenshots", "attributes": {} }] }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }, { "id": "approved", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "approved", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var delayCount = 0;
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            var result = await service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                ReplaceExisting = true,
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
                            Path = "iphone-6-5"
                        }
                    ]
                }
            });

            Assert.Equal(2, delayCount);
            Assert.Equal("approved", Assert.Single(Assert.Single(result.ScreenshotSets).Uploaded).Screenshot.Id);
            Assert.Equal(10, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingRejectsKnownAssetWithWrongChecksumWithoutPolling()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "approved", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var delayCount = 0;
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SyncAsync(CreateSingleSetReplacementRequest(root.FullName)));

            Assert.Contains("changed during replacement", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, delayCount);
            Assert.Equal(8, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingBoundsConvergenceByElapsedTime()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": {} }, { "id": "approved", "type": "appScreenshots", "attributes": {} }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                static (_, token) => Task.Delay(TimeSpan.FromMinutes(1), token),
                replacementInventoryTimeout: TimeSpan.FromMilliseconds(30));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SyncAsync(CreateSingleSetReplacementRequest(root.FullName)));

            Assert.Contains("did not converge within", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(8, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingUsesConfiguredDeadlineBeyondThirtyPolls()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var responses = new List<SequenceResponse>
            {
                new(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac")
            };
            for (var index = 0; index < 31; index++)
            {
                responses.Add(new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": {} }, { "id": "approved", "type": "appScreenshots", "attributes": {} }] }"""));
            }
            responses.Add(new SequenceResponse(HttpStatusCode.OK,
                """{ "data": [{ "id": "approved", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""));

            var handler = new SequenceHandler(responses.ToArray());
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var delayCount = 0;
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                replacementInventoryTimeout: TimeSpan.FromSeconds(5));

            var result = await service.SyncAsync(CreateSingleSetReplacementRequest(root.FullName));

            Assert.Equal(31, delayCount);
            Assert.Equal("approved", Assert.Single(Assert.Single(result.ScreenshotSets).Uploaded).Screenshot.Id);
            Assert.Equal(39, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingPropagatesCallerCancellation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": {} }, { "id": "approved", "type": "appScreenshots", "attributes": {} }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            using var cancellationSource = new CancellationTokenSource();
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                (_, token) =>
                {
                    cancellationSource.Cancel();
                    return Task.Delay(TimeSpan.FromMinutes(1), token);
                },
                replacementInventoryTimeout: TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.SyncAsync(CreateSingleSetReplacementRequest(root.FullName), cancellationSource.Token));
            Assert.Equal(8, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingDoesNotRelabelIndependentOperationCancellationAsConvergenceTimeout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-1", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("approved", "01-home.png", 3),
                ScreenshotCommit("approved", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK,
                    """{ "data": [{ "id": "old", "type": "appScreenshots", "attributes": {} }, { "id": "approved", "type": "appScreenshots", "attributes": {} }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                static (_, _) => Task.FromException(new OperationCanceledException("independent timeout")),
                replacementInventoryTimeout: TimeSpan.FromSeconds(5));

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.SyncAsync(CreateSingleSetReplacementRequest(root.FullName)));

            Assert.Equal("independent timeout", exception.Message);
            Assert.Equal(8, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingRejectsLateDriftAcrossScreenshotSets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var phone = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var tablet = Directory.CreateDirectory(Path.Combine(root.FullName, "ipad-13"));
            await File.WriteAllBytesAsync(Path.Combine(phone.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(Path.Combine(tablet.FullName, "01-home.png"), new byte[] { 4, 5, 6 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-phone", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }, { "id": "set-tablet", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPAD_PRO_3GEN_129" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-phone-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-tablet-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("new-phone", "01-home.png", 3),
                ScreenshotCommit("new-phone", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("new-tablet", "01-home.png", 3),
                ScreenshotCommit("new-tablet", "01-home.png", "b4a3ba90641372b4e4eaa841a5a400ec"),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "b4a3ba90641372b4e4eaa841a5a400ec" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }, { "id": "concurrent", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppStoreConnectScreenshotSyncService(client).SyncAsync(new AppStoreConnectScreenshotSyncRequest
                {
                    BaseDirectory = root.FullName,
                    ReplaceExisting = true,
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
                                Path = "iphone-6-5"
                            },
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPAD_PRO_3GEN_129",
                                Path = "ipad-13"
                            }
                        ]
                    }
                }));

            Assert.Contains("changed after screenshot replacement", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(14, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_ReplaceExistingWaitsForBenignStaleCrossSetInventory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var phone = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var tablet = Directory.CreateDirectory(Path.Combine(root.FullName, "ipad-13"));
            await File.WriteAllBytesAsync(Path.Combine(phone.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(Path.Combine(tablet.FullName, "01-home.png"), new byte[] { 4, 5, 6 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "version-1", "type": "appStoreVersions", "attributes": { "versionString": "1.0.0", "platform": "IOS" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "loc-1", "type": "appStoreVersionLocalizations", "attributes": { "locale": "en-US" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "set-phone", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPHONE_65" } }, { "id": "set-tablet", "type": "appScreenshotSets", "attributes": { "screenshotDisplayType": "APP_IPAD_PRO_3GEN_129" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-phone-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-tablet-checksum" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("new-phone", "01-home.png", 3),
                ScreenshotCommit("new-phone", "01-home.png", "5289df737df57326fcdd22597afb1fac"),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""),
                new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
                ScreenshotReservation("new-tablet", "01-home.png", 3),
                ScreenshotCommit("new-tablet", "01-home.png", "b4a3ba90641372b4e4eaa841a5a400ec"),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "b4a3ba90641372b4e4eaa841a5a400ec" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "old-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "old-phone-checksum" } }, { "id": "new-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "b4a3ba90641372b4e4eaa841a5a400ec" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-phone", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac" } }] }"""),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "new-tablet", "type": "appScreenshots", "attributes": { "sourceFileChecksum": "b4a3ba90641372b4e4eaa841a5a400ec" } }] }"""));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var delayCount = 0;
            var service = new AppStoreConnectScreenshotSyncService(
                client,
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            var result = await service.SyncAsync(CreateTwoSetReplacementRequest(root.FullName));

            Assert.Equal(1, delayCount);
            Assert.Equal(2, result.ScreenshotSets.Length);
            Assert.Equal(17, handler.RequestUris.Count);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static AppStoreConnectScreenshotSyncRequest CreateSingleSetReplacementRequest(string baseDirectory)
        => new()
        {
            BaseDirectory = baseDirectory,
            ReplaceExisting = true,
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
                        Path = "iphone-6-5"
                    }
                ]
            }
        };

    private static AppStoreConnectScreenshotSyncRequest CreateTwoSetReplacementRequest(string baseDirectory)
        => new()
        {
            BaseDirectory = baseDirectory,
            ReplaceExisting = true,
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
                        Path = "iphone-6-5"
                    },
                    new AppStoreConnectScreenshotSetSyncSpec
                    {
                        ScreenshotDisplayType = "APP_IPAD_PRO_3GEN_129",
                        Path = "ipad-13"
                    }
                ]
            }
        };
}
