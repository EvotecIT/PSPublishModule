using System.Text.Json;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteLocalizationFeaturesTests
{
    [Fact]
    public void Build_LocalizedPages_EmitHreflangHeadLinks_AndLanguageSearchShards()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-features-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Build Test", "localization-features-theme");
            var result = BuildSite(root, spec);

            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");
            Assert.True(File.Exists(enHtmlPath));
            Assert.True(File.Exists(plHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            Assert.Contains("hreflang=\"en\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"pl\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"x-default\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://example.test/docs/\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://example.test/pl/docs/\"", enHtml, StringComparison.OrdinalIgnoreCase);

            var allSearchPath = Path.Combine(result.OutputPath, "search", "index.json");
            var enSearchPath = Path.Combine(result.OutputPath, "search", "en", "index.json");
            var plSearchPath = Path.Combine(result.OutputPath, "search", "pl", "index.json");
            var manifestPath = Path.Combine(result.OutputPath, "search", "manifest.json");
            var collectionSearchPath = Path.Combine(result.OutputPath, "search", "collections", "docs", "index.json");
            var searchSurfacePath = Path.Combine(result.OutputPath, "search", "index.html");
            Assert.True(File.Exists(allSearchPath));
            Assert.True(File.Exists(enSearchPath));
            Assert.True(File.Exists(plSearchPath));
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(collectionSearchPath));
            Assert.True(File.Exists(searchSurfacePath));

            var allEntries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            var enEntries = JsonDocument.Parse(File.ReadAllText(enSearchPath)).RootElement.EnumerateArray().ToArray();
            var plEntries = JsonDocument.Parse(File.ReadAllText(plSearchPath)).RootElement.EnumerateArray().ToArray();

            Assert.Equal(2, allEntries.Length);
            Assert.Single(enEntries);
            Assert.Single(plEntries);
            Assert.All(enEntries, entry => Assert.Equal("en", entry.GetProperty("language").GetString()));
            Assert.All(plEntries, entry => Assert.Equal("pl", entry.GetProperty("language").GetString()));
            Assert.All(allEntries, entry => Assert.Equal("docs:index", entry.GetProperty("translationKey").GetString()));

            var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement;
            Assert.Equal("/search/index.json", manifest.GetProperty("searchIndexPath").GetString());
            Assert.Contains(
                manifest.GetProperty("languageShards").EnumerateArray().ToArray(),
                shard => string.Equals(shard.GetProperty("language").GetString(), "en", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                manifest.GetProperty("collectionShards").EnumerateArray().ToArray(),
                shard => string.Equals(shard.GetProperty("collection").GetString(), "docs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_EmitHreflangHeadLinks_WithPerLanguageBaseUrls()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-features-domain-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Domain Build Test", "localization-features-domain-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";

            var result = BuildSite(root, spec);
            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            Assert.True(File.Exists(enHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            Assert.Contains("href=\"https://evotec.xyz/docs/\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://evotec.pl/pl/docs/\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"x-default\" href=\"https://evotec.xyz/docs/\"", enHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_SupportSingleLanguageDomainStyleBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-single-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-features-single-language-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Single-Language Build Test", "localization-features-single-language-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";

            var result = BuildSite(root, spec, language: "pl", languageAsRoot: true);
            var plRootHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            var plPrefixedHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");

            Assert.True(File.Exists(plRootHtmlPath));
            Assert.False(File.Exists(plPrefixedHtmlPath));

            var html = File.ReadAllText(plRootHtmlPath);
            Assert.Contains("Docs PL", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://evotec.pl/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://evotec.xyz/docs/\"", html, StringComparison.OrdinalIgnoreCase);

            var allSearchPath = Path.Combine(result.OutputPath, "search", "index.json");
            var plSearchPath = Path.Combine(result.OutputPath, "search", "pl", "index.json");
            Assert.True(File.Exists(allSearchPath));
            Assert.True(File.Exists(plSearchPath));
            var allEntries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            var plEntries = JsonDocument.Parse(File.ReadAllText(plSearchPath)).RootElement.EnumerateArray().ToArray();
            Assert.Single(allEntries);
            Assert.Single(plEntries);
            Assert.Equal("pl", allEntries[0].GetProperty("language").GetString());
            Assert.Equal("pl", plEntries[0].GetProperty("language").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_RebaseInternalLinks_ForRootLanguageBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-rebase-root-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsPlPath);

            File.WriteAllText(Path.Combine(docsPlPath, "index.md"),
                """
                ---
                title: Docs PL
                ---

                [Body link](/pl/docs/other/)
                """);

            File.WriteAllText(Path.Combine(docsPlPath, "other.md"),
                """
                ---
                title: Other PL
                slug: other
                ---

                # Other PL
                """);

            var themeRoot = Path.Combine(root, "themes", "localization-rebase-root-theme");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));

            File.WriteAllText(Path.Combine(themeRoot, "layouts", "docs.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <link rel="alternate" href="https://evotec.pl/pl/docs/other/" />
                  <link rel="preconnect" href="https://cdn.example.test" crossorigin="anonymous" />
                  {{ head_html }}
                </head>
                <body>
                  {{ include "header" }}
                  <main>{{ content }}</main>
                  <footer>
                    <a class="footer-link" href="/pl/contact/">Kontakt</a>
                    <a class="footer-link-absolute" href="https://evotec.pl/pl/contact/">Kontakt absolute</a>
                  </footer>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(themeRoot, "partials", "header.html"),
                """
                <header>
                  <a class="brand" href="/pl/">Start</a>
                  <nav>{{ pf.nav_links "main-pl" }}</nav>
                </header>
                """);

            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "localization-rebase-root-theme",
                  "engine": "scriban",
                  "defaultLayout": "docs"
                }
                """);

            File.WriteAllText(Path.Combine(themeRoot, "assets", "app.css"), "body { font-family: Segoe UI, Arial, sans-serif; }");

            var spec = CreateLocalizedDocsSpec("Localization Root Link Rebase Test", "localization-rebase-root-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Localization.Languages[1].RenderAtRoot = true;
            spec.Navigation = new NavigationSpec
            {
                AutoDefaults = false,
                Menus = new[]
                {
                    new MenuSpec
                    {
                        Name = "main-pl",
                        Items = new[]
                        {
                            new MenuItemSpec { Title = "Start", Url = "/pl/" },
                            new MenuItemSpec { Title = "Docs", Url = "/pl/docs/" },
                            new MenuItemSpec { Title = "Other", Url = "/pl/docs/other/" }
                        }
                    }
                }
            };

            var result = BuildSite(root, spec, language: "pl", languageAsRoot: true);
            var plHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            Assert.True(File.Exists(plHtmlPath));

            var html = File.ReadAllText(plHtmlPath);
            Assert.Contains("class=\"brand\" href=\"/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Start</a>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Docs</a>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/other/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Other</a>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/other/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("class=\"footer-link\" href=\"/contact/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://evotec.pl/docs/other/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://evotec.pl/contact/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"/pl/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"/pl/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"/pl/contact/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"/https://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://evotec.pl/pl/docs/other/", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://evotec.pl/pl/contact/", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_EmitXDefault_WhenOnlyCurrentLanguageAlternateExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-current-xdefault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsPlPath);
            CreateSimpleTheme(root, "localization-current-xdefault-theme", "docs");

            File.WriteAllText(Path.Combine(docsPlPath, "tylko-pl.md"),
                """
                ---
                title: Tylko PL
                slug: tylko-pl
                translation_key: docs:tylko-pl
                ---

                # Tylko PL
                """);

            var spec = CreateLocalizedDocsSpec("Localization Current X-Default Test", "localization-current-xdefault-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";

            var result = BuildSite(root, spec);
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "tylko-pl", "index.html");
            Assert.True(File.Exists(plHtmlPath));

            var plHtml = File.ReadAllText(plHtmlPath);
            Assert.Contains("hreflang=\"pl\" href=\"https://evotec.pl/pl/docs/tylko-pl/\"", plHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"x-default\" href=\"https://evotec.pl/pl/docs/tylko-pl/\"", plHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hreflang=\"en\" href=\"https://evotec.xyz/docs/tylko-pl/\"", plHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedFallbackPages_KeepLocalizedRoutesAndSelfCanonicalize()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDefaultLanguageOnlyDocsContent(root);
            CreateSeoTheme(root, "localization-fallback-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Fallback Build Test", "localization-fallback-theme");
            spec.Localization!.FallbackToDefaultLanguage = true;
            spec.Localization.MaterializeFallbackPages = true;
            spec.Localization.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";

            var result = BuildSite(root, spec);

            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");
            Assert.True(File.Exists(enHtmlPath));
            Assert.True(File.Exists(plHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            var plHtml = File.ReadAllText(plHtmlPath);

            Assert.Contains("hreflang=\"en\" href=\"https://evotec.xyz/docs", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"pl\" href=\"https://evotec.pl/pl/docs", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<link rel=\"canonical\" href=\"https://evotec.xyz/docs/\" />", enHtml, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("hreflang=\"en\" href=\"https://evotec.xyz/docs", plHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"pl\" href=\"https://evotec.pl/pl/docs", plHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<link rel=\"canonical\" href=\"https://evotec.pl/pl/docs/\" />", plHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<link rel=\"canonical\" href=\"https://evotec.xyz/docs/\" />", plHtml, StringComparison.OrdinalIgnoreCase);

            var allSearchPath = Path.Combine(result.OutputPath, "search", "index.json");
            Assert.True(File.Exists(allSearchPath));
            var entries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            Assert.Contains(entries, entry =>
                string.Equals(entry.GetProperty("url").GetString()?.TrimEnd('/'), "/docs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.GetProperty("language").GetString(), "en", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(entries, entry =>
                string.Equals(entry.GetProperty("url").GetString()?.TrimEnd('/'), "/pl/docs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.GetProperty("language").GetString(), "pl", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(entries, entry =>
                string.Equals(entry.GetProperty("url").GetString()?.TrimEnd('/'), "/pl/docs", StringComparison.OrdinalIgnoreCase) &&
                entry.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("i18n.fallback_copy", out var fallbackFlag) &&
                fallbackFlag.ValueKind == JsonValueKind.True);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedFallbackPages_CanBeDisabledPerCollection()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-build-fallback-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDefaultLanguageOnlyDocsContent(root);
            CreateSeoTheme(root, "localization-fallback-disabled-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Fallback Disabled Test", "localization-fallback-disabled-theme");
            spec.Localization!.FallbackToDefaultLanguage = true;
            spec.Localization.MaterializeFallbackPages = true;
            spec.Localization.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Collections[0].MaterializeFallbackPages = false;

            var result = BuildSite(root, spec);

            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");
            Assert.True(File.Exists(enHtmlPath));
            Assert.False(File.Exists(plHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            Assert.DoesNotContain("hreflang=\"pl\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<link rel=\"canonical\" href=\"https://evotec.xyz/docs/\" />", enHtml, StringComparison.OrdinalIgnoreCase);

            var allSearchPath = Path.Combine(result.OutputPath, "search", "index.json");
            Assert.True(File.Exists(allSearchPath));
            var entries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            Assert.Single(entries);
            Assert.Equal("en", entries[0].GetProperty("language").GetString());
            Assert.DoesNotContain(entries, entry =>
                string.Equals(entry.GetProperty("url").GetString()?.TrimEnd('/'), "/pl/docs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedFallbackPages_MaterializeForSelectedRootLanguageBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-root-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDefaultLanguageOnlyDocsContent(root);
            CreateSimpleTheme(root, "localization-root-fallback-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Root Fallback Build Test", "localization-root-fallback-theme");
            spec.Localization!.FallbackToDefaultLanguage = true;
            spec.Localization.MaterializeFallbackPages = true;
            spec.Localization.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Localization.Languages[1].RenderAtRoot = true;

            var result = BuildSite(root, spec, language: "pl", languageAsRoot: true);

            var plRootHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            var plPrefixedHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");
            Assert.True(File.Exists(plRootHtmlPath));
            Assert.False(File.Exists(plPrefixedHtmlPath));

            var html = File.ReadAllText(plRootHtmlPath);
            Assert.Contains("Docs EN", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"pl\" href=\"https://evotec.pl/docs", html, StringComparison.OrdinalIgnoreCase);

            var allSearchPath = Path.Combine(result.OutputPath, "search", "index.json");
            Assert.True(File.Exists(allSearchPath));
            var entries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            var entry = Assert.Single(entries);
            Assert.Equal("pl", entry.GetProperty("language").GetString());
            Assert.Equal("/docs/", entry.GetProperty("url").GetString());
            Assert.True(
                entry.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("i18n.fallback_copy", out var fallbackFlag) &&
                fallbackFlag.ValueKind == JsonValueKind.True);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Sitemap_Generate_FromEntriesJson_ProducesJsonAndThemedHtmlMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-sitemap-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-sitemap-json-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Sitemap JSON Test", "localization-sitemap-json-theme");
            var build = BuildSite(root, spec);

            var entriesPath = Path.Combine(root, "sitemap.entries.json");
            File.WriteAllText(entriesPath,
                """
                {
                  "entries": [
                    {
                      "path": "/docs/",
                      "title": "Documentation Home",
                      "section": "Guides",
                      "description": "Landing page for documentation."
                    }
                  ]
                }
                """);

            var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = build.OutputPath,
                BaseUrl = spec.BaseUrl,
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                IncludeLanguageAlternates = false,
                EntriesJsonPath = entriesPath,
                GenerateJson = true,
                GenerateHtml = true
            });

            Assert.True(File.Exists(sitemap.OutputPath));
            Assert.True(File.Exists(sitemap.JsonOutputPath));
            Assert.True(File.Exists(sitemap.HtmlOutputPath));

            var json = JsonDocument.Parse(File.ReadAllText(sitemap.JsonOutputPath!)).RootElement;
            var docsEntry = json.GetProperty("entries")
                .EnumerateArray()
                .FirstOrDefault(entry => string.Equals(entry.GetProperty("path").GetString(), "/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Documentation Home", docsEntry.GetProperty("title").GetString());
            Assert.Equal("Guides", docsEntry.GetProperty("section").GetString());

            var html = File.ReadAllText(sitemap.HtmlOutputPath!);
            Assert.Contains("Documentation Home", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Guides", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/themes/localization-sitemap-json-theme/assets/app.css", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Sitemap_Generate_EmitsLocalizedAlternates_WhenSiteSpecAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-sitemap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-sitemap-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Sitemap Test", "localization-sitemap-theme");
            var result = BuildSite(root, spec);
            var siteSpecPath = Path.Combine(result.OutputPath, "_powerforge", "site-spec.json");
            Assert.True(File.Exists(siteSpecPath));

            var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = result.OutputPath,
                BaseUrl = spec.BaseUrl,
                IncludeTextFiles = false
            });
            Assert.True(File.Exists(sitemap.OutputPath));

            var doc = XDocument.Load(sitemap.OutputPath);
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var xhtmlNs = XNamespace.Get("http://www.w3.org/1999/xhtml");
            var docsEntry = doc
                .Descendants(sitemapNs + "url")
                .FirstOrDefault(url => string.Equals(
                    url.Element(sitemapNs + "loc")?.Value,
                    "https://example.test/docs/",
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(docsEntry);
            var alternates = docsEntry!
                .Elements(xhtmlNs + "link")
                .Select(element => new
                {
                    HrefLang = element.Attribute("hreflang")?.Value,
                    Href = element.Attribute("href")?.Value
                })
                .ToArray();

            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "en", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://example.test/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "pl", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://example.test/pl/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "x-default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://example.test/docs/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Sitemap_Generate_EmitsLocalizedAlternates_WithPerLanguageBaseUrls()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-sitemap-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-sitemap-domain-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Sitemap Domain Test", "localization-sitemap-domain-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";

            var result = BuildSite(root, spec);
            var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = result.OutputPath,
                BaseUrl = spec.BaseUrl,
                IncludeTextFiles = false
            });
            Assert.True(File.Exists(sitemap.OutputPath));

            var doc = XDocument.Load(sitemap.OutputPath);
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var xhtmlNs = XNamespace.Get("http://www.w3.org/1999/xhtml");
            var docsEntry = doc
                .Descendants(sitemapNs + "url")
                .FirstOrDefault(url => string.Equals(
                    url.Element(sitemapNs + "loc")?.Value,
                    "https://example.test/docs/",
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(docsEntry);
            var alternates = docsEntry!
                .Elements(xhtmlNs + "link")
                .Select(element => new
                {
                    HrefLang = element.Attribute("hreflang")?.Value,
                    Href = element.Attribute("href")?.Value
                })
                .ToArray();

            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "en", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.xyz/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "pl", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.pl/pl/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "x-default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.xyz/docs/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Sitemap_Generate_EmitsLocalizedAlternates_WithRootServedLanguageBaseUrls()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-sitemap-root-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-sitemap-root-domain-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Features Sitemap Root Domain Test", "localization-sitemap-root-domain-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Localization.Languages[1].RenderAtRoot = true;

            var result = BuildSite(root, spec);
            var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = result.OutputPath,
                BaseUrl = spec.BaseUrl,
                IncludeTextFiles = false
            });
            Assert.True(File.Exists(sitemap.OutputPath));

            var doc = XDocument.Load(sitemap.OutputPath);
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var xhtmlNs = XNamespace.Get("http://www.w3.org/1999/xhtml");
            var docsEntry = doc
                .Descendants(sitemapNs + "url")
                .FirstOrDefault(url => string.Equals(
                    url.Element(sitemapNs + "loc")?.Value,
                    "https://example.test/docs/",
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(docsEntry);
            var alternates = docsEntry!
                .Elements(xhtmlNs + "link")
                .Select(element => new
                {
                    HrefLang = element.Attribute("hreflang")?.Value,
                    Href = element.Attribute("href")?.Value
                })
                .ToArray();

            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "en", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.xyz/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "pl", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.pl/docs/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(alternates, alt =>
                string.Equals(alt.HrefLang, "x-default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alt.Href, "https://evotec.xyz/docs/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_RespectExplicitI18nRoutes_WhenTranslationKeysDiffer()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-explicit-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsEnPath = Path.Combine(root, "content", "docs", "en");
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsEnPath);
            Directory.CreateDirectory(docsPlPath);

            File.WriteAllText(Path.Combine(docsEnPath, "faq.md"),
                """
                ---
                title: FAQ EN
                slug: faq-english
                translation_key: docs:faq-en
                i18n.routes.pl: /pl/docs/faq-polski/
                ---

                # FAQ EN
                """);

            File.WriteAllText(Path.Combine(docsPlPath, "faq.md"),
                """
                ---
                title: FAQ PL
                slug: faq-polski
                translation_key: docs:faq-pl
                i18n.routes.en: /docs/faq-english/
                ---

                # FAQ PL
                """);

            CreateSimpleTheme(root, "localization-explicit-routes-theme", "docs");
            var spec = CreateLocalizedDocsSpec("Localization Explicit Route Test", "localization-explicit-routes-theme");
            var result = BuildSite(root, spec);

            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "faq-english", "index.html");
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "faq-polski", "index.html");
            Assert.True(File.Exists(enHtmlPath));
            Assert.True(File.Exists(plHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            var plHtml = File.ReadAllText(plHtmlPath);
            Assert.Contains("hreflang=\"pl\" href=\"https://example.test/pl/docs/faq-polski/\"", enHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"en\" href=\"https://example.test/docs/faq-english/\"", plHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_ApplyPerLanguageAliases_ToRedirects()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-language-aliases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsEnPath = Path.Combine(root, "content", "docs", "en");
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsEnPath);
            Directory.CreateDirectory(docsPlPath);

            File.WriteAllText(Path.Combine(docsEnPath, "index.md"),
                """
                ---
                title: Docs EN
                ---

                # Docs EN
                """);

            File.WriteAllText(Path.Combine(docsPlPath, "faq.md"),
                """
                ---
                title: FAQ PL
                slug: faq-polski
                aliases:
                  - /legacy/shared-faq/
                i18n.aliases.pl:
                  - /pl/legacy/faq/
                i18n.aliases.en:
                  - /docs/legacy-faq-en/
                ---

                # FAQ PL
                """);

            CreateSimpleTheme(root, "localization-language-aliases-theme", "docs");
            var spec = CreateLocalizedDocsSpec("Localization Language Alias Test", "localization-language-aliases-theme");
            var result = BuildSite(root, spec);

            Assert.True(File.Exists(result.RedirectsPath));
            using var redirectsDoc = JsonDocument.Parse(File.ReadAllText(result.RedirectsPath));
            var redirects = redirectsDoc.RootElement.GetProperty("redirects").EnumerateArray().ToArray();

            static bool HasRedirect(JsonElement[] rows, string from, string to) =>
                rows.Any(row =>
                    string.Equals(row.GetProperty("from").GetString(), from, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.GetProperty("to").GetString(), to, StringComparison.OrdinalIgnoreCase));

            Assert.True(HasRedirect(redirects, "/legacy/shared-faq", "/pl/docs/faq-polski/"));
            Assert.True(HasRedirect(redirects, "/pl/legacy/faq", "/pl/docs/faq-polski/"));
            Assert.False(HasRedirect(redirects, "/docs/legacy-faq-en", "/pl/docs/faq-polski/"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_EmitAlternateHeadLinks_ForRootServedLanguage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-root-alt-head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSimpleTheme(root, "localization-root-alt-head-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Root Alternate Head Test", "localization-root-alt-head-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Localization.Languages[1].RenderAtRoot = true;

            var result = BuildSite(root, spec);
            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "index.html");
            Assert.True(File.Exists(enHtmlPath));

            var html = File.ReadAllText(enHtmlPath);
            Assert.Contains("hreflang=\"en\" href=\"https://evotec.xyz/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hreflang=\"pl\" href=\"https://evotec.pl/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hreflang=\"pl\" href=\"https://evotec.pl/pl/docs/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_EmitCanonicalAndSocialUrls_ForRootServedLanguage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-root-seo-head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateLocalizedDocsContent(root);
            CreateSeoTheme(root, "localization-root-seo-head-theme", "docs");

            var spec = CreateLocalizedDocsSpec("Localization Root SEO Head Test", "localization-root-seo-head-theme");
            spec.Localization!.Languages[0].BaseUrl = "https://evotec.xyz";
            spec.Localization.Languages[1].BaseUrl = "https://evotec.pl";
            spec.Localization.Languages[1].RenderAtRoot = true;
            spec.Social = new SocialSpec
            {
                Enabled = true,
                Image = "/assets/social/default.png"
            };

            var result = BuildSite(root, spec);
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "index.html");
            Assert.True(File.Exists(plHtmlPath));

            var html = File.ReadAllText(plHtmlPath);
            Assert.Contains("<link rel=\"canonical\" href=\"https://evotec.pl/docs/\" />", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<meta property=\"og:url\" content=\"https://evotec.pl/docs/\" />", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<meta name=\"twitter:url\" content=\"https://evotec.pl/docs/\" />", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://evotec.pl/pl/docs/", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizedPages_SupportNestedLocalizationBlocks_ForRoutesAndAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-nested-blocks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsEnPath = Path.Combine(root, "content", "docs", "en");
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsEnPath);
            Directory.CreateDirectory(docsPlPath);

            File.WriteAllText(Path.Combine(docsEnPath, "faq.md"),
                """
                ---
                title: FAQ EN
                slug: faq-english
                i18n:
                  group: docs:faq-en
                translations:
                  pl:
                    route: /pl/docs/faq-polski/
                  en:
                    aliases:
                      - /docs/stare-faq/
                ---

                # FAQ EN
                """);

            File.WriteAllText(Path.Combine(docsPlPath, "faq.md"),
                """
                ---
                title: FAQ PL
                slug: faq-polski
                i18n:
                  group: docs:faq-pl
                translations:
                  en:
                    route: /docs/faq-english/
                ---

                # FAQ PL
                """);

            CreateSimpleTheme(root, "localization-nested-blocks-theme", "docs");
            var spec = CreateLocalizedDocsSpec("Localization Nested Blocks Test", "localization-nested-blocks-theme");
            var result = BuildSite(root, spec);

            var enHtmlPath = Path.Combine(result.OutputPath, "docs", "faq-english", "index.html");
            var plHtmlPath = Path.Combine(result.OutputPath, "pl", "docs", "faq-polski", "index.html");
            Assert.True(File.Exists(enHtmlPath));
            Assert.True(File.Exists(plHtmlPath));

            var enHtml = File.ReadAllText(enHtmlPath);
            Assert.Contains("hreflang=\"pl\" href=\"https://example.test/pl/docs/faq-polski/\"", enHtml, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(result.RedirectsPath));
            using var redirectsDoc = JsonDocument.Parse(File.ReadAllText(result.RedirectsPath));
            var redirects = redirectsDoc.RootElement.GetProperty("redirects").EnumerateArray().ToArray();
            Assert.Contains(redirects, row =>
                string.Equals(row.GetProperty("from").GetString(), "/docs/stare-faq", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.GetProperty("to").GetString(), "/docs/faq-english/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsForLocalizationDuplicateAndMissingTranslations()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsEnPath = Path.Combine(root, "content", "docs", "en");
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsEnPath);
            Directory.CreateDirectory(docsPlPath);

            File.WriteAllText(Path.Combine(docsEnPath, "faq-a.md"),
                """
                ---
                title: FAQ A
                translation_key: docs:faq
                ---

                FAQ A
                """);
            File.WriteAllText(Path.Combine(docsEnPath, "faq-b.md"),
                """
                ---
                title: FAQ B
                translation_key: docs:faq
                ---

                FAQ B
                """);
            File.WriteAllText(Path.Combine(docsPlPath, "faq.md"),
                """
                ---
                title: FAQ PL
                translation_key: docs:faq
                ---

                FAQ PL
                """);

            var spec = new SiteSpec
            {
                Name = "Localization Features Verify Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Default = true },
                        new LanguageSpec { Code = "pl" },
                        new LanguageSpec { Code = "de" }
                    }
                },
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verification = WebSiteVerifier.Verify(spec, plan);

            Assert.Contains(
                verification.Warnings,
                warning => warning.Contains("[PFWEB.LOCALIZATION]", StringComparison.OrdinalIgnoreCase) &&
                           warning.Contains("duplicate translation mapping for key 'docs:faq'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                verification.Warnings,
                warning => warning.Contains("[PFWEB.LOCALIZATION]", StringComparison.OrdinalIgnoreCase) &&
                           warning.Contains("translation 'docs:faq' is missing languages [de]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RespectsCollectionExpectedTranslationLanguages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-collection-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogEnPath = Path.Combine(root, "content", "blog", "en");
            var blogPlPath = Path.Combine(root, "content", "blog", "pl");
            Directory.CreateDirectory(blogEnPath);
            Directory.CreateDirectory(blogPlPath);

            File.WriteAllText(Path.Combine(blogEnPath, "post-a.md"),
                """
                ---
                title: Post A
                date: 2026-01-01
                translation_key: wp-post-demo
                ---

                Post A
                """);
            File.WriteAllText(Path.Combine(blogPlPath, "post-a.md"),
                """
                ---
                title: Post A PL
                date: 2026-01-02
                translation_key: wp-post-demo
                ---

                Post A PL
                """);

            var spec = new SiteSpec
            {
                Name = "Localization Features Collection Language Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Default = true },
                        new LanguageSpec { Code = "pl" },
                        new LanguageSpec { Code = "de" },
                        new LanguageSpec { Code = "fr" }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Preset = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ExpectedTranslationLanguages = new[] { "en", "pl" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verification = WebSiteVerifier.Verify(spec, plan);

            Assert.DoesNotContain(
                verification.Warnings,
                warning => warning.Contains("[PFWEB.LOCALIZATION]", StringComparison.OrdinalIgnoreCase) &&
                           warning.Contains("translation 'wp-post-demo' is missing languages", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsForLocalizationConfigGaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-features-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                ---

                # Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Localization Features Config Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "de",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Default = true, BaseUrl = "not-a-valid-url" },
                        new LanguageSpec { Code = "pl", Default = true }
                    }
                },
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var verification = WebSiteVerifier.Verify(spec, plan);

            Assert.Contains(
                verification.Warnings,
                warning => warning.Contains("Localization defines multiple default languages", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                verification.Warnings,
                warning => warning.Contains("defaultLanguage 'de' does not match any active language code", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                verification.Warnings,
                warning => warning.Contains("defines invalid BaseUrl", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static WebBuildResult BuildSite(string root, SiteSpec spec, string? language = null, bool languageAsRoot = false)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var outPath = Path.Combine(root, "_site");
        var plan = WebSitePlanner.Plan(spec, configPath);
        return WebSiteBuilder.Build(spec, plan, outPath, language: language, languageAsRoot: languageAsRoot);
    }

    private static SiteSpec CreateLocalizedDocsSpec(string name, string themeName)
    {
        return new SiteSpec
        {
            Name = name,
            BaseUrl = "https://example.test",
            ContentRoot = "content",
            DefaultTheme = themeName,
            ThemesRoot = "themes",
            TrailingSlash = TrailingSlashMode.Always,
            Features = new[] { "docs", "search" },
            Localization = new LocalizationSpec
            {
                Enabled = true,
                DefaultLanguage = "en",
                PrefixDefaultLanguage = false,
                DetectFromPath = true,
                Languages = new[]
                {
                    new LanguageSpec { Code = "en", Label = "English", Default = true },
                    new LanguageSpec { Code = "pl", Label = "Polski" }
                }
            },
            Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "docs",
                    Input = "content/docs",
                    Output = "/docs",
                    DefaultLayout = "docs"
                }
            }
        };
    }

    private static void CreateSimpleTheme(string root, string themeName, string layoutName)
    {
        var themeRoot = Path.Combine(root, "themes", themeName);
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", $"{layoutName}.html"),
            """
            <!doctype html>
            <html>
            <head>{{ head_html }}</head>
            <body>{{ content }}</body>
            </html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            $$"""
            {
              "name": "{{themeName}}",
              "engine": "scriban",
              "defaultLayout": "{{layoutName}}"
            }
            """);
        File.WriteAllText(Path.Combine(themeRoot, "assets", "app.css"), "body { font-family: Segoe UI, Arial, sans-serif; }");
    }

    private static void CreateSeoTheme(string root, string themeName, string layoutName)
    {
        var themeRoot = Path.Combine(root, "themes", themeName);
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", $"{layoutName}.html"),
            """
            <!doctype html>
            <html>
            <head>
            {{ canonical_html }}
            {{ opengraph_html }}
            {{ structured_data_html }}
            {{ head_html }}
            </head>
            <body>{{ content }}</body>
            </html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            $$"""
            {
              "name": "{{themeName}}",
              "engine": "scriban",
              "defaultLayout": "{{layoutName}}"
            }
            """);
        File.WriteAllText(Path.Combine(themeRoot, "assets", "app.css"), "body { font-family: Segoe UI, Arial, sans-serif; }");
    }

    private static void CreateLocalizedDocsContent(string root)
    {
        var docsEnPath = Path.Combine(root, "content", "docs", "en");
        var docsPlPath = Path.Combine(root, "content", "docs", "pl");
        Directory.CreateDirectory(docsEnPath);
        Directory.CreateDirectory(docsPlPath);
        File.WriteAllText(Path.Combine(docsEnPath, "index.md"),
            """
            ---
            title: Docs EN
            ---

            # Docs EN
            """);
        File.WriteAllText(Path.Combine(docsPlPath, "index.md"),
            """
            ---
            title: Docs PL
            ---

            # Docs PL
            """);
    }

    private static void CreateDefaultLanguageOnlyDocsContent(string root)
    {
        var docsEnPath = Path.Combine(root, "content", "docs", "en");
        Directory.CreateDirectory(docsEnPath);
        File.WriteAllText(Path.Combine(docsEnPath, "index.md"),
            """
            ---
            title: Docs EN
            ---

            # Docs EN
            """);
    }
}
