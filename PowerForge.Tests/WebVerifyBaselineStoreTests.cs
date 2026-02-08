using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebVerifyBaselineStoreTests
{
    [Fact]
    public void VerifyBaseline_WriteAndLoad_RoundTripsKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var baselinePath = Path.Combine(root, "verify-baseline.json");
            var written = WebVerifyBaselineStore.Write(root, baselinePath, new[]
            {
                " [PFWEB.X] One ",
                "[PFWEB.Y] Two",
                "[PFWEB.Y] Two" // duplicate
            }, mergeWithExisting: false, logger: null);

            Assert.True(File.Exists(written));

            var keys = WebVerifyBaselineStore.LoadWarningKeysSafe(root, baselinePath);
            Assert.Contains(keys, k => k.Contains("PFWEB.X", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(keys, k => k.Contains("PFWEB.Y", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, keys.Length);

            using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
            Assert.True(doc.RootElement.TryGetProperty("warningKeys", out var arr));
            Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VerifyBaseline_Update_MergesWithExisting()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-baseline-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var baselinePath = Path.Combine(root, "verify-baseline.json");
            WebVerifyBaselineStore.Write(root, baselinePath, new[] { "A" }, mergeWithExisting: false, logger: null);
            WebVerifyBaselineStore.Write(root, baselinePath, new[] { "B" }, mergeWithExisting: true, logger: null);

            var keys = WebVerifyBaselineStore.LoadWarningKeysSafe(root, baselinePath);
            Assert.Contains(keys, k => string.Equals(k, "A", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(keys, k => string.Equals(k, "B", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, keys.Length);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VerifyBaseline_PathOutsideRoot_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-baseline-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outside = Path.Combine(Path.GetTempPath(), "pf-web-verify-baseline-outside-" + Guid.NewGuid().ToString("N") + ".json");
            Assert.Throws<InvalidOperationException>(() => WebVerifyBaselineStore.ResolveBaselinePath(root, outside));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

