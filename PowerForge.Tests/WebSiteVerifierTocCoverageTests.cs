using PowerForge.Web;
using System.Text.Json;
using YamlDotNet.Serialization;

public class WebSiteVerifierTocCoverageTests
{
    [Fact]
    public void Verify_AcceptsExistingTocTargetsInOverlappingLanguageRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-language-toc-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content");
        try
        {
            foreach (var language in new[] { "en", "pl" })
            {
                Directory.CreateDirectory(Path.Combine(content, language));
                File.WriteAllText(Path.Combine(content, language, "index.md"),
                    $"---\ntitle: Home\nlanguage: {language}\n---\n# Home");
            }
            File.WriteAllText(Path.Combine(content, "toc.json"),
                """[{"href":"/"},{"href":"/pl/"},{"href":"/missing/"}]""");
            var spec = new SiteSpec
            {
                Name = "Overlapping TOC roots", BaseUrl = "https://example.test", TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true, DetectFromPath = true, DefaultLanguage = "en",
                    Languages = [new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }]
                },
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/", UseToc = true }]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            Assert.Equal("TOC for collection 'pages' contains 1 missing page(s): /missing/",
                Assert.Single(result.Warnings, w => w.StartsWith("TOC for collection 'pages'", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(TrailingSlashMode.Always)]
    [InlineData(TrailingSlashMode.Ignore)]
    public void Verify_ResolvesSharedTocSeparatelyForEachProject(TrailingSlashMode slashMode)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-project-toc-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var project in new[] { "alpha", "beta" })
            {
                var content = Path.Combine(root, "projects", project, "content");
                Directory.CreateDirectory(content);
                File.WriteAllText(Path.Combine(root, "projects", project, "project.json"),
                    JsonSerializer.Serialize(new { name = project, slug = project }));
                File.WriteAllText(Path.Combine(content, "index.md"), "---\ntitle: Home\n---\n# Home");
                if (project == "alpha")
                {
                    File.WriteAllText(Path.Combine(content, "install.md"), "---\ntitle: Install\n---\n# Install");
                    File.WriteAllText(Path.Combine(content, "unlisted.md"), "---\ntitle: Unlisted\n---\n# Unlisted");
                }
            }
            File.WriteAllText(Path.Combine(root, "toc.json"), """
                [{"href":"./"},{"href":"install.md#requirements"},{"href":"missing/"}]
                """);
            var spec = new SiteSpec
            {
                Name = "Project TOC coverage", BaseUrl = "https://example.test", ProjectsRoot = "projects",
                TrailingSlash = slashMode,
                Collections = [new CollectionSpec
                {
                    Name = "projects", Input = "projects/*/content", Output = "/projects/{project}/docs",
                    UseToc = true, TocFile = "toc.json"
                }]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            var warnings = result.Warnings.Where(w => w.StartsWith("TOC for collection 'projects'", StringComparison.Ordinal)).ToArray();
            var suffix = slashMode == TrailingSlashMode.Always ? "/" : "";
            Assert.Equal(3, warnings.Length);
            Assert.Contains($"TOC for collection 'projects' is missing 1 page(s): /projects/alpha/docs/unlisted{suffix}", warnings);
            Assert.Contains($"TOC for collection 'projects' contains 1 missing page(s): /projects/alpha/docs/missing{suffix}", warnings);
            var betaWarning = Assert.Single(warnings, w => w.StartsWith("TOC for collection 'projects' contains 2 missing page(s):", StringComparison.Ordinal));
            Assert.Contains($"/projects/beta/docs/install{suffix}", betaWarning, StringComparison.Ordinal);
            Assert.Contains($"/projects/beta/docs/missing{suffix}", betaWarning, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/docs", TrailingSlashMode.Always, "json", false)]
    [InlineData("/docs", TrailingSlashMode.Never, "json", false)]
    [InlineData("/docs", TrailingSlashMode.Always, "yml", false)]
    [InlineData("/docs", TrailingSlashMode.Never, "yml", false)]
    [InlineData("/docs#api", TrailingSlashMode.Always, "json", false)]
    [InlineData("/docs#api", TrailingSlashMode.Never, "yml", false)]
    [InlineData("/docs%20api", TrailingSlashMode.Always, "json", false)]
    [InlineData("/", TrailingSlashMode.Always, "json", false)]
    [InlineData("/", TrailingSlashMode.Never, "json", false)]
    [InlineData("/", TrailingSlashMode.Always, "yml", false)]
    [InlineData("/", TrailingSlashMode.Never, "yml", false)]
    [InlineData("/docs", TrailingSlashMode.Ignore, "json", false)]
    [InlineData("/", TrailingSlashMode.Ignore, "yml", false)]
    [InlineData("/docs#api", TrailingSlashMode.Always, "json", true)]
    [InlineData("/docs", TrailingSlashMode.Ignore, "yml", true)]
    public void Verify_ResolvesCollectionRelativeTocLinksAndRetainsRealCoverageWarnings(
        string output, TrailingSlashMode slashMode, string format, bool localized)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-toc-coverage-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content", "docs");
        Directory.CreateDirectory(Path.Combine(content, "guides"));
        try
        {
            foreach (var page in new[] { "index", "install", "overview", "guides/index", "guides/setup", "rooted", "unlisted" })
                File.WriteAllText(Path.Combine(content, page + ".md"), $"---\ntitle: {page}\nlanguage: {(localized ? "pl" : "en")}\n---\n\n# {page}\n");

            var prefix = (localized ? "/pl" : "") + output.TrimEnd('/');
            var urlPrefix = string.Join("/", prefix.Split('/').Select(Uri.EscapeDataString));
            var items = new List<object>
            {
                new { title = "Home", href = "./" },
                new { title = "Home source", url = "index.md" },
                new { title = "Install", href = "install.md#requirements" },
                new { title = "Overview", href = "overview/?view=all#details" },
                new { title = "Guides", href = "guides/index.md", items = new[]
                {
                    new { title = "Setup", href = "./guides/setup.md#steps" }
                } },
                new { title = "Rooted", href = urlPrefix + "/rooted/" },
                new { title = "Section", href = "#local-section" },
                new { title = "External", href = "https://example.test/external/" },
                new { title = "External CDN", href = "//example.test/cdn/" },
                new { title = "Broken", href = "missing/" }
            };
            if (output != "/")
                items.Add(new { title = "Other collection", href = "/docs-other/outside/" });

            var tocText = format == "json"
                ? JsonSerializer.Serialize(items)
                : new SerializerBuilder().Build().Serialize(items);
            var tocPath = Path.Combine(content, "toc." + format);
            File.WriteAllText(tocPath, tocText);
            var spec = new SiteSpec
            {
                Name = "TOC coverage",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = slashMode,
                Localization = new LocalizationSpec
                {
                    Enabled = localized, DefaultLanguage = "en",
                    Languages = [new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }]
                },
                Collections = [new CollectionSpec
                {
                    Name = "docs", Input = "content/docs", Output = output, UseToc = true
                }]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            var warnings = result.Warnings.Where(w => w.StartsWith("TOC for collection 'docs'", StringComparison.Ordinal)).ToArray();
            var suffix = slashMode == TrailingSlashMode.Always ? "/" : "";
            Assert.Equal(2, warnings.Length);
            Assert.Contains($"TOC for collection 'docs' is missing 1 page(s): {prefix}/unlisted{suffix}", warnings);
            Assert.Contains($"TOC for collection 'docs' contains 1 missing page(s): {prefix}/missing{suffix}", warnings);
            Assert.Equal(tocText, File.ReadAllText(tocPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
