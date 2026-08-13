using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Fact]
    public void Scan_ReportsArtifactReadFailureAndContinuesWithOtherArtifacts()
    {
        var root = CreateArtifact("locked.txt", "dotnet add package hidden-package");
        File.WriteAllText(Path.Combine(root, "readable.txt"), "plain documentation");
        using var lockStream = new FileStream(
            Path.Combine(root, "locked.txt"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        using var scanner = new WebAgentContentSecurityScanner();

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "locked.txt", "readable.txt" }
            });

            Assert.False(result.Success);
            Assert.Equal(1, result.ArtifactCount);
            Assert.Contains(result.Findings, issue =>
                issue.Code == "PFAGENT.ARTIFACT.READ_FAILED" && issue.Path == "locked.txt");
        }
        finally
        {
            lockStream.Dispose();
            TryDeleteDirectory(root);
        }
    }
}
