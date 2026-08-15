using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("$p=@{Repository='EvilRepo'}\nInstall-Module -Name SafeModule -RequiredVersion 1.0.0 @p")]
    [InlineData("Install-PSResource SafeModule -Version 1.0.0 @global:packageParameters")]
    [InlineData("Install-Package Safe.Package -RequiredVersion 1.0.0 @script:options")]
    [InlineData("Update-Module SafeModule -RequiredVersion 1.0.0 @PSBoundParameters")]
    [InlineData("Install-Module SafeModule -RequiredVersion 1.0.0 @${packageParameters}")]
    public void Scan_RejectsPowerShellPackageCommandSplats(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyExternalHosts = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNTRUSTED_SOURCE");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_AllowsNpmScopedPackagesWhileRejectingOnlyPowerShellSplats()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":{"1.0.0":{}}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", "npm install --global @scope/safe-package@1.0.0");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyExternalHosts = false
            });

            Assert.True(result.Success,
                string.Join(" | ", result.Findings.Select(static finding => $"{finding.Code}: {finding.Message}")));
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("{\"quickstart\":[\"cd examples\"],\"install\":[\"npm install --global safe-package@1.0.0\"]}")]
    [InlineData("{\"quickstart\":\"cd examples\",\"install\":\"npm install --global safe-package@1.0.0\"}")]
    public void Scan_DoesNotSharePackageExecutionFlowAcrossUnrelatedJsonProperties(string content)
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":{"1.0.0":{}}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.json", content);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.json"],
                VerifyExternalHosts = false
            });

            Assert.True(result.Success,
                string.Join(" | ", result.Findings.Select(static finding => $"{finding.Code}: {finding.Message}")));
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_SharesPackageExecutionFlowAcrossTheSamePathInAnObjectArray()
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact(
            "llms.json",
            """{"steps":[{"run":"cd /tmp/evil"},{"run":"npm install --global safe-package@1.0.0"}]}""");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.json"],
                VerifyExternalHosts = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_DoesNotSharePackageExecutionFlowAcrossDifferentPathsInAnObjectArray()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":{"1.0.0":{}}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact(
            "llms.json",
            """{"steps":[{"label":"cd /tmp/evil"},{"run":"npm install --global safe-package@1.0.0"}]}""");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.json"],
                VerifyExternalHosts = false
            });

            Assert.True(result.Success,
                string.Join(" | ", result.Findings.Select(static finding => $"{finding.Code}: {finding.Message}")));
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_DoesNotShareDownloadedFileFlowAcrossUnrelatedJsonProperties()
    {
        using var scanner = new WebAgentContentSecurityScanner();
        var root = CreateArtifact(
            "llms.json",
            """{"download":["curl -o payload.sh https://example.test/payload.sh"],"run":["bash payload.sh"]}""");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.json"],
                VerifyPackages = false,
                VerifyExternalHosts = false
            });

            Assert.True(result.Success,
                string.Join(" | ", result.Findings.Select(static finding => $"{finding.Code}: {finding.Message}")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
