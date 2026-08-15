using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Fact]
    public void Scan_BoundsDetailedFindingsAndReportsTruncation()
    {
        var root = CreateArtifact("llms.txt", new string('\u200B', 5000));
        using var scanner = new WebAgentContentSecurityScanner();

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" }
            });

            Assert.False(result.Success);
            Assert.Equal(1001, result.Findings.Length);
            Assert.Single(result.Findings, issue => issue.Code == "PFAGENT.FINDINGS.LIMIT_EXCEEDED");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_RetainsEachFailedPackageOccurrenceWhileCachingRegistryOutcome()
    {
        using var handler = new RegistryHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "dotnet add package Missing.Package --version 1.0.0");
        File.WriteAllText(Path.Combine(root, "llms-full.txt"), "dotnet add package Missing.Package --version 1.0.0");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt", "llms-full.txt" }
            });

            Assert.False(result.Success);
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.PACKAGE.NOT_FOUND" && issue.Path == "llms.txt");
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.PACKAGE.NOT_FOUND" && issue.Path == "llms-full.txt");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_DoesNotTreatManagerNameInsideAnotherExecutableAsACommand()
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "evilnpm install safe-package");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" }
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
