namespace PowerForge.Tests;

public sealed partial class DotNetPublishReleaseArtifactVerifierTests
{
    private const string ChecksumDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData('*')]
    [InlineData(' ')]
    public void ChecksumContains_AcceptsGnuBinaryAndTextMarkersWithoutTrimmingPath(char marker)
    {
        string catalog = Path.GetTempFileName();
        try
        {
            const string relativePath = " artifacts/release file.zip";
            File.WriteAllText(catalog, $"{ChecksumDigest} {marker}{relativePath}{Environment.NewLine}");

            Assert.True(DotNetPublishReleaseArtifactVerifier.ChecksumContains(
                catalog,
                relativePath,
                ChecksumDigest.ToUpperInvariant()));
        }
        finally
        {
            File.Delete(catalog);
        }
    }

    [Theory]
    [InlineData("\t")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ChecksumContains_RejectsMissingOrAmbiguousModeMarker(string separator)
    {
        string catalog = Path.GetTempFileName();
        try
        {
            const string relativePath = "artifacts/release.zip";
            File.WriteAllText(catalog, ChecksumDigest + separator + relativePath + Environment.NewLine);

            Assert.False(DotNetPublishReleaseArtifactVerifier.ChecksumContains(
                catalog,
                relativePath,
                ChecksumDigest));
        }
        finally
        {
            File.Delete(catalog);
        }
    }

    [Fact]
    public void ChecksumContains_RejectsDuplicateTargetEntries()
    {
        string catalog = Path.GetTempFileName();
        try
        {
            const string relativePath = "artifacts/release.zip";
            File.WriteAllLines(catalog, new[]
            {
                $"{ChecksumDigest} *{relativePath}",
                $"{ChecksumDigest}  {relativePath}"
            });

            Assert.False(DotNetPublishReleaseArtifactVerifier.ChecksumContains(
                catalog,
                relativePath,
                ChecksumDigest));
        }
        finally
        {
            File.Delete(catalog);
        }
    }

    [Fact]
    public void ChecksumContains_UsesCatalogFilesystemCaseSemantics()
    {
        string catalog = Path.GetTempFileName();
        try
        {
            File.WriteAllText(catalog, $"{ChecksumDigest} *artifacts/Release.zip{Environment.NewLine}");

            bool result = DotNetPublishReleaseArtifactVerifier.ChecksumContains(
                catalog,
                "artifacts/release.zip",
                ChecksumDigest);

            StringComparison comparison = FrameworkCompatibility.GetPathStringComparisonForPath(catalog);
            Assert.Equal(comparison == StringComparison.OrdinalIgnoreCase, result);
        }
        finally
        {
            File.Delete(catalog);
        }
    }
}
