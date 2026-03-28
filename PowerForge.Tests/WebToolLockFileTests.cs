using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebToolLockFileTests
{
    [Fact]
    public void Normalize_AppliesDefaults_AndPreservesExplicitValues()
    {
        var normalized = WebToolLockFile.Normalize(new WebToolLockSpec
        {
            Tag = "PowerForgeWeb-v1.0.0-preview",
            Asset = "PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.tar.gz",
            BinaryPath = "PowerForgeWeb"
        });

        Assert.Equal(WebToolLockFile.DefaultSchemaUrl, normalized.Schema);
        Assert.Equal(WebToolLockFile.DefaultRepository, normalized.Repository);
        Assert.Equal(WebToolLockFile.DefaultTarget, normalized.Target);
        Assert.Equal("PowerForgeWeb-v1.0.0-preview", normalized.Tag);
        Assert.Equal("PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.tar.gz", normalized.Asset);
        Assert.Equal("PowerForgeWeb", normalized.BinaryPath);
    }

    [Fact]
    public void Read_NormalizesMinimalToolLock()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "powerforge-tool-lock-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tempPath, """
            {
              "repository": "EvotecIT/PSPublishModule",
              "tag": "PowerForgeWeb-v1.0.0-preview",
              "asset": "PowerForgeWeb-1.0.0-net10.0-win-x64-SingleContained.zip"
            }
            """);

            var parsed = WebToolLockFile.Read(tempPath);

            Assert.Equal(WebToolLockFile.DefaultSchemaUrl, parsed.Schema);
            Assert.Equal("EvotecIT/PSPublishModule", parsed.Repository);
            Assert.Equal(WebToolLockFile.DefaultTarget, parsed.Target);
            Assert.Equal("PowerForgeWeb-v1.0.0-preview", parsed.Tag);
            Assert.Equal("PowerForgeWeb-1.0.0-net10.0-win-x64-SingleContained.zip", parsed.Asset);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void Validate_ReportsMissingTagAndAsset()
    {
        var issues = WebToolLockFile.Validate(new WebToolLockSpec
        {
            Repository = "EvotecIT/PSPublishModule"
        });

        Assert.Contains("tool lock is missing 'tag'.", issues);
        Assert.Contains("tool lock is missing 'asset'.", issues);
    }
}
