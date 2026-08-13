using System.Net;
using System.Reflection;
using System.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("dotnet.exe tool install Sample.Tool --version 1.0.0", "nuget")]
    [InlineData("python.exe -m pip install sample-tool==1.0.0", "pypi")]
    [InlineData("pip.exe install sample-tool==1.0.0", "pypi")]
    [InlineData("npm.cmd install sample-tool@1.0.0", "npm")]
    [InlineData("python3.12.exe -m pip install sample-tool==1.0.0", "pypi")]
    [InlineData("pip3.12 install sample-tool==1.0.0", "pypi")]
    public void Scan_NormalizesWindowsExecutableSuffixes(string command, string ecosystem)
    {
        using var handler = new RegistryHandler(_ => ecosystem switch
        {
            "pypi" => JsonResponse("""{"releases":{"1.0.0":[]}}"""),
            "npm" => JsonResponse("""{"versions":{"1.0.0":{}}}"""),
            _ => JsonResponse("""{"versions":["1.0.0"]}""")
        });
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", command);

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" }
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_RefusesNetworkWorkWhenPackageReferenceLimitIsExceeded()
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "npm install first-package second-package");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" },
                MaxPackageReferences = 1
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.PACKAGE.LIMIT_EXCEEDED");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_DeduplicatesPackageIdentityAcrossGeneratedArtifactsBeforeApplyingLimit()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":["1.0.0"]}"""));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "dotnet add package Safe.Package --version 1.0.0");
        File.Copy(Path.Combine(root, "llms.txt"), Path.Combine(root, "llms-full.txt"));
        File.WriteAllText(Path.Combine(root, "llms.json"),
            """{"installation":"dotnet add package Safe.Package --version 1.0.0"}""");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt", "llms-full.txt", "llms.json" },
                MaxPackageReferences = 1
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, result.VerifiedPackageCount);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_RejectsOversizedDecompressedRegistryResponse()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":["1.0.0"]}"""));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "dotnet add package Safe.Package --version 1.0.0");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" },
                MaxRegistryResponseBytes = 8
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.PACKAGE.REGISTRY_RESPONSE_TOO_LARGE");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[64:ff9b::7f00:1]/")]
    [InlineData("http://[2002:7f00:1::]/")]
    [InlineData("http://[fc00::1]/")]
    public void Scan_RejectsNonPublicAndEmbeddedIpv6HostsBeforeHttp(string url)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", url);

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" },
                VerifyPackages = false,
                VerifyExternalHosts = true
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.HOST.NON_PUBLIC");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_RefusesHostWorkWhenExternalHostLimitIsExceeded()
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt", "https://one.example.test/ https://two.example.test/");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" },
                VerifyPackages = false,
                VerifyExternalHosts = true,
                MaxExternalHosts = 1
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.HOST.LIMIT_EXCEEDED");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_ReportsWholeNetworkBudgetBeforeSkippingConfiguredHostChecks()
    {
        using var handler = new DelayedRegistryHandler(
            TimeSpan.FromMilliseconds(1100),
            JsonResponse("""{"versions":["1.0.0"]}"""));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var root = CreateArtifact("llms.txt",
            "dotnet add package Safe.Package --version 1.0.0\nhttp://127.0.0.1/");

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = new[] { "llms.txt" },
                VerifyExternalHosts = true,
                RequestTimeoutSeconds = 5,
                MaxNetworkDurationSeconds = 1
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, issue => issue.Code == "PFAGENT.NETWORK.TIME_BUDGET");
            Assert.DoesNotContain(result.Findings, issue => issue.Code == "PFAGENT.HOST.NON_PUBLIC");
            Assert.Equal(0, result.ExternalHostCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HostFingerprintProbe_PreservesObservedHttpScheme()
    {
        using var handler = new RegistryHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var scanner = new WebAgentContentSecurityScanner(client);
        var method = typeof(WebAgentContentSecurityScanner).GetMethod(
            "VerifyTakeoverFingerprint",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var findings = new List<WebAgentContentSecurityFinding>();

        Assert.NotNull(method);
        method!.Invoke(scanner, new object[]
        {
            new Uri("http://example.test/"),
            new[] { IPAddress.Parse("203.0.113.10") },
            5,
            findings,
            CancellationToken.None
        });

        Assert.Equal("http", handler.LastRequestUri!.Scheme);
        Assert.Empty(findings);
    }

    [Fact]
    public void HostAddressPolicy_RejectsLocalUseNat64Prefix()
    {
        var method = typeof(WebAgentContentSecurityScanner).GetMethod(
            "IsPublicAddress",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var isPublic = (bool)method!.Invoke(null, new object[] { IPAddress.Parse("64:ff9b:1::c000:201") })!;
        Assert.False(isPublic);
    }

    [Fact]
    public void Audit_UsesFinalArtifactOwnerCatalogGate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-agent-audit-" + Guid.NewGuid().ToString("N"));
        var site = Path.Combine(root, "_site");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "index.html"), "<html><head><title>Test</title></head><body><nav></nav><h1>Test</h1></body></html>");
        File.WriteAllText(Path.Combine(site, "llms.txt"), "dotnet add package evotec.xyz --version 1.0.0");
        File.WriteAllText(Path.Combine(root, "catalog.json"),
            """
            {
              "nuget": {
                "owner": "EvotecIT",
                "packages": [{ "id": "Real.Package", "version": "1.0.0" }]
              },
              "warnings": []
            }
            """);

        try
        {
            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = site,
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                AgentContentSecurity = new WebAgentContentSecurityOptions
                {
                    Files = new[] { "llms.txt" },
                    PublicationCatalogPath = Path.Combine(root, "catalog.json"),
                    NuGetOwner = "EvotecIT",
                    RequireOwnerVerification = new[] { "nuget:*" }
                }
            });

            Assert.False(result.Success);
            Assert.Equal(1, result.AgentPackageReferenceCount);
            Assert.Contains(result.Issues, issue =>
                issue.Category == "agent-content" &&
                issue.Hint.Contains("pfagent-package-owner-mismatch", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Audit_UsesDistinctBaselineKeysForSameCodeFindingsInOneArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-agent-baseline-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"),
            "<html><head><title>Test</title></head><body><nav></nav><h1>Test</h1></body></html>");
        File.WriteAllText(Path.Combine(root, "llms.txt"),
            "dotnet add package $firstPackage\ndotnet add package $secondPackage");

        try
        {
            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                AgentContentSecurity = new WebAgentContentSecurityOptions
                {
                    Files = new[] { "llms.txt" },
                    VerifyPackages = false
                }
            });

            var issues = result.Issues
                .Where(issue => issue.Category == "agent-content" &&
                                issue.Hint.Contains("unverifiable-operand", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, issues.Length);
            Assert.Equal(2, issues.Select(issue => issue.Key).Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("sub.example.com", "example.com", false)]
    [InlineData("sub.example.com", ".example.com", true)]
    public void TrustedDomainContract_DistinguishesExactAndSubdomainEntries(
        string host,
        string configured,
        bool expected)
    {
        var method = typeof(WebAgentContentSecurityScanner).GetMethod(
            "IsTrustedDomain",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, new object?[] { host, new[] { configured } })!);
    }

    [Fact]
    public void Audit_DoesNotMutateReusableAgentContentOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-agent-options-" + Guid.NewGuid().ToString("N"));
        var site = Path.Combine(root, "_site");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "index.html"),
            "<html><head><title>Test</title></head><body><nav></nav><h1>Test</h1></body></html>");
        File.WriteAllText(Path.Combine(site, "llms.txt"), "No installation instructions.");
        var agentOptions = new WebAgentContentSecurityOptions
        {
            SiteRoot = "sentinel",
            Files = new[] { "llms.txt" },
            VerifyPackages = false
        };

        try
        {
            WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = site,
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                AgentContentSecurity = agentOptions
            });

            Assert.Equal("sentinel", agentOptions.SiteRoot);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

}
