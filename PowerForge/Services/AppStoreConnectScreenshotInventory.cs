using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>Computes the stable ordered identity of remote App Store screenshot sets.</summary>
internal static class AppStoreConnectScreenshotInventory
{
    internal static string ComputeSha256(
        IEnumerable<AppStoreConnectReleaseScreenshotSetReadiness> screenshotSets)
    {
        var canonical = screenshotSets
            .OrderBy(static set => set.ScreenshotDisplayType, StringComparer.Ordinal)
            .Select(static set => new
            {
                set.ScreenshotDisplayType,
                set.ScreenshotSetId,
                Screenshots = (set.Screenshots ?? Array.Empty<AppStoreConnectReleaseScreenshotAssetReadiness>()).Select(static screenshot => new
                {
                    screenshot.Id,
                    screenshot.FileName,
                    screenshot.FileSize,
                    screenshot.SourceFileChecksum,
                    screenshot.AssetDeliveryState
                }).ToArray()
            })
            .ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(canonical);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(payload)).Replace("-", string.Empty);
    }
}
