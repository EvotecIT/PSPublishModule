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
            Assert.True(File.Exists(allSearchPath));
            Assert.True(File.Exists(enSearchPath));
            Assert.True(File.Exists(plSearchPath));

            var allEntries = JsonDocument.Parse(File.ReadAllText(allSearchPath)).RootElement.EnumerateArray().ToArray();
            var enEntries = JsonDocument.Parse(File.ReadAllText(enSearchPath)).RootElement.EnumerateArray().ToArray();
            var plEntries = JsonDocument.Parse(File.ReadAllText(plSearchPath)).RootElement.EnumerateArray().ToArray();

            Assert.Equal(2, allEntries.Length);
            Assert.Single(enEntries);
            Assert.Single(plEntries);
            Assert.All(enEntries, entry => Assert.Equal("en", entry.GetProperty("language").GetString()));
            Assert.All(plEntries, entry => Assert.Equal("pl", entry.GetProperty("language").GetString()));
            Assert.All(allEntries, entry => Assert.Equal("docs:index", entry.GetProperty("translationKey").GetString()));
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
                        new LanguageSpec { Code = "en", Default = true },
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
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static WebBuildResult BuildSite(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var outPath = Path.Combine(root, "_site");
        var plan = WebSitePlanner.Plan(spec, configPath);
        return WebSiteBuilder.Build(spec, plan, outPath);
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
}
