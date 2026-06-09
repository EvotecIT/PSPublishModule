using PowerForge.Web;

namespace PowerForge.Tests;

public class WebXrefTests
{
    [Fact]
    public void Build_ResolvesInternalXrefFromFrontMatterId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-internal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Install](xref:docs.install)
                """);
            File.WriteAllText(Path.Combine(docsPath, "install.md"),
                """
                ---
                title: Install
                slug: install
                xref: docs.install
                ---

                Install steps.
                """);

            var spec = BuildDocsSpec();
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var docsHome = File.ReadAllText(Path.Combine(result.OutputPath, "docs", "index.html"));
            Assert.Contains("href=\"/docs/install/\"", docsHome, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ResolvesXrefFromExternalMap_AndEmitsXrefMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [String.Length](xref:System.String#Length)
                """);
            File.WriteAllText(Path.Combine(root, "xrefmap.json"),
                """
                {
                  "references": [
                    {
                      "uid": "System.String",
                      "href": "/api/dotnet/System.String/"
                    }
                  ]
                }
                """);

            var spec = BuildDocsSpec();
            spec.Xref = new XrefSpec
            {
                MapFiles = new[] { "xrefmap.json" },
                EmitMap = true
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var docsHome = File.ReadAllText(Path.Combine(result.OutputPath, "docs", "index.html"));
            Assert.Contains("href=\"/api/dotnet/System.String/#Length\"", docsHome, StringComparison.OrdinalIgnoreCase);

            var emittedMap = Path.Combine(result.OutputPath, "_powerforge", "xrefmap.json");
            Assert.True(File.Exists(emittedMap), "Expected _powerforge/xrefmap.json to be written when Xref.EmitMap=true.");
            var xrefMapJson = File.ReadAllText(emittedMap);
            Assert.Contains("System.String", xrefMapJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsOnUnresolvedXref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-verify-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Missing](xref:unknown.symbol)
                """);

            var spec = BuildDocsSpec();
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.True(verify.Success);
            Assert.Contains(verify.Warnings, warning =>
                warning.Contains("[PFWEB.XREF]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("unknown.symbol", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhenXrefResolvesToKnownPageId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-verify-known-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Install](xref:docs.install)
                """);
            File.WriteAllText(Path.Combine(docsPath, "install.md"),
                """
                ---
                title: Install
                slug: install
                xref: docs.install
                ---

                Install steps.
                """);

            var spec = BuildDocsSpec();
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.True(verify.Success);
            Assert.DoesNotContain(verify.Warnings, warning => warning.Contains("[PFWEB.XREF]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnAboutMissingMapWhenXrefDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-verify-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Install](xref:docs.install)
                """);

            var spec = BuildDocsSpec();
            spec.Xref = new XrefSpec
            {
                Enabled = false,
                MapFiles = new[] { "missing-xref-map.json" }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.True(verify.Success);
            Assert.DoesNotContain(verify.Warnings, warning =>
                warning.Contains("missing-xref-map.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RecognizesMarkdownXrefLinkWithTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-verify-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Install](xref:docs.install "Install guide")
                """);
            File.WriteAllText(Path.Combine(docsPath, "install.md"),
                """
                ---
                title: Install
                slug: install
                xref: docs.install
                ---

                Install steps.
                """);

            var spec = BuildDocsSpec();
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.True(verify.Success);
            Assert.DoesNotContain(verify.Warnings, warning => warning.Contains("[PFWEB.XREF]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ResolvesXrefFromExternalEntriesPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-entries-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);

            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs Home
                slug: index
                ---

                [Install](xref:docs.install)
                """);
            File.WriteAllText(Path.Combine(root, "xrefmap.json"),
                """
                {
                  "entries": [
                    {
                      "id": "docs.install",
                      "url": "/docs/install/"
                    }
                  ]
                }
                """);

            var spec = BuildDocsSpec();
            spec.Xref = new XrefSpec
            {
                MapFiles = new[] { "xrefmap.json" }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var docsHome = File.ReadAllText(Path.Combine(result.OutputPath, "docs", "index.html"));
            Assert.Contains("href=\"/docs/install/\"", docsHome, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static SiteSpec BuildDocsSpec()
    {
        return new SiteSpec
        {
            Name = "Web Xref Test",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
            Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "docs",
                    Input = "content/docs",
                    Output = "/docs"
                }
            }
        };
    }
}
