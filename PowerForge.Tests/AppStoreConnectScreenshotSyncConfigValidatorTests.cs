namespace PowerForge.Tests;

public sealed class AppStoreConnectScreenshotSyncConfigValidatorTests
{
    [Fact]
    public void Validate_QualityGateRejectsDuplicatePngs()
    {
        var root = CreateSandbox();
        try
        {
            WritePngHeader(Path.Combine(root, "01.png"), 1290, 2796);
            File.Copy(Path.Combine(root, "01.png"), Path.Combine(root, "02.png"));
            var result = Validate(root, new[] { "1290x2796" });

            Assert.False(result.IsValid);
            Assert.Contains(
                Assert.Single(result.ScreenshotSets).Messages,
                message => message.Contains("duplicates", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Validate_QualityGateRejectsUnexpectedDimensions()
    {
        var root = CreateSandbox();
        try
        {
            WritePngHeader(Path.Combine(root, "01.png"), 1179, 2556);
            var result = Validate(root, new[] { "1290x2796" });

            Assert.False(result.IsValid);
            Assert.Contains(
                Assert.Single(result.ScreenshotSets).Messages,
                message => message.Contains("allowed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static AppStoreConnectScreenshotSyncValidationResult Validate(string root, string[] allowed)
        => new AppStoreConnectScreenshotSyncConfigValidator().Validate(
            new AppStoreConnectScreenshotSyncSpec
            {
                AppId = "1234567890",
                VersionString = "1.0.0",
                ScreenshotSets = new[]
                {
                    new AppStoreConnectScreenshotSetSyncSpec
                    {
                        ScreenshotDisplayType = "APP_IPHONE_65",
                        Path = root,
                        AllowedDimensions = allowed
                    }
                },
                Quality = new AppStoreConnectScreenshotQualitySpec
                {
                    Enabled = true,
                    MinimumFileBytes = 0,
                    MinimumKilobytesPerMegapixel = 0
                }
            },
            root);

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.ScreenshotQuality", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePngHeader(string path, int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
