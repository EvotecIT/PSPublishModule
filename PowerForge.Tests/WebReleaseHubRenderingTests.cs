using PowerForge.Web;

namespace PowerForge.Tests;

public class WebReleaseHubRenderingTests
{
    [Fact]
    public void Build_RendersPfReleaseHelpers_FromReleaseHubData()
    {
        var html = BuildSinglePageSite(
            """
            # Release Hub
            """,
            setup: WriteReleaseHubData,
            useScribanTheme: true,
            scribanLayoutBody: """
            <section class="cta">{{ pf.release_button "intelligencex.chat" "stable" "windows" "x64" "zip" "Download Chat" "btn btn-primary" }}</section>
            <section class="matrix">{{ pf.release_buttons "intelligencex.chat" "stable" 5 "platform" }}</section>
            <section class="timeline">{{ pf.release_changelog "intelligencex.chat" 10 true }}</section>
            """);

        Assert.Contains("Download Chat", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-release-platform=\"windows\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Platform: Windows", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Latest Stable", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersReleaseShortcodes_FromReleaseHubData()
    {
        var html = BuildSinglePageSite(
            """
            {{< release-button product="intelligencex.chat" channel="stable" platform="windows" arch="x64" kind="zip" label="Get Chat" class="btn btn-primary" >}}
            {{< release-buttons product="intelligencex.chat" channel="stable" groupBy="platform" limit="2" >}}
            {{< release-changelog product="intelligencex.chat" limit="1" includePreview="false" >}}
            """,
            setup: WriteReleaseHubData,
            useScribanTheme: false,
            scribanLayoutBody: null);

        Assert.Contains("Get Chat", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-buttons", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Platform: Windows", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1.2.0", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1.3.0-preview1", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersReleaseShortcodes_FromPlacementConfig()
    {
        var html = BuildSinglePageSite(
            """
            {{< release-button placement="home.chat_primary" >}}
            {{< release-buttons placement="home.chat_matrix" >}}
            {{< release-changelog placement="changelog.chat_timeline" >}}
            """,
            setup: root =>
            {
                WriteReleaseHubData(root);
                WriteReleasePlacementData(root);
            },
            useScribanTheme: false,
            scribanLayoutBody: null);

        Assert.Contains("Get Chat (Placement)", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-buttons", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Platform: Windows", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1.2.0", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1.3.0-preview1", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersReleaseButtons_ForAllProducts_WithWildcardFilter()
    {
        var html = BuildSinglePageSite(
            """
            {{< release-buttons product="*" channel="stable" groupBy="product" limit="10" >}}
            """,
            setup: WriteReleaseHubData,
            useScribanTheme: false,
            scribanLayoutBody: null);

        Assert.Contains("Product: intelligencex.chat", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product: intelligencex.docs", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IntelligenceX.Chat-win-x64-v1.2.0.zip", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IntelligenceX.Docs-win-x64-v1.2.0.zip", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersReleaseAssetBadgesAndThumbnails()
    {
        var html = BuildSinglePageSite(
            """
            {{< release-buttons product="intelligencex.chat" channel="stable" limit="2" >}}
            {{< release-changelog product="intelligencex.chat" limit="1" includePreview="false" >}}
            """,
            setup: WriteReleaseHubData,
            useScribanTheme: false,
            scribanLayoutBody: null);

        Assert.Contains("pf-release-button-thumb", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix-chat-win-thumb.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-badge--platform", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-badge--arch", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-badge--kind", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-asset-thumb", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-release-asset-badge--platform", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSinglePageSite(
        string markdown,
        Action<string> setup,
        bool useScribanTheme,
        string? scribanLayoutBody)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-release-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            setup(root);

            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                $$"""
                ---
                title: Home
                slug: index
                ---

                {{markdown}}
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(themeRoot);
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));

            if (useScribanTheme)
            {
                File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                    """
                    {
                      "schemaVersion": 2,
                      "contractVersion": 2,
                      "name": "t",
                      "engine": "scriban",
                      "layoutsPath": "layouts",
                      "defaultLayout": "home"
                    }
                    """);

                var scribanLayout = """
                    <!doctype html>
                    <html>
                    <head><title>{{ page.title }}</title></head>
                    <body>
                      {{ content }}
                      __PF_RELEASE_HELPER_BODY__
                    </body>
                    </html>
                    """.Replace("__PF_RELEASE_HELPER_BODY__", scribanLayoutBody ?? string.Empty, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), scribanLayout);
            }
            else
            {
                File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                    """
                    <!doctype html>
                    <html>
                    <head><title>{{TITLE}}</title>{{EXTRA_CSS}}</head>
                    <body>{{CONTENT}}{{EXTRA_SCRIPTS}}</body>
                    </html>
                    """);
                File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                    """
                    {
                      "name": "t",
                      "engine": "simple",
                      "defaultLayout": "home"
                    }
                    """);
            }

            var spec = new SiteSpec
            {
                Name = "Release Hub Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
                DefaultTheme = "t",
                ThemesRoot = "themes",
                ThemeEngine = useScribanTheme ? "scriban" : "simple",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "home",
                        Include = new[] { "*.md", "**/*.md" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);

            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            var indexHtml = Path.Combine(outPath, "index.html");
            Assert.True(File.Exists(indexHtml), "Expected index.html to be generated.");
            return File.ReadAllText(indexHtml);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void WriteReleaseHubData(string root)
    {
        var dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "release-hub.json"),
            """
            {
              "title": "IntelligenceX Releases",
              "products": [
                { "id": "intelligencex.chat", "name": "IX Chat", "order": 10 },
                { "id": "intelligencex.docs", "name": "IX Docs", "order": 20 }
              ],
              "releases": [
                {
                  "tag": "v1.2.0",
                  "title": "IntelligenceX 1.2.0",
                  "url": "https://github.com/EvotecIT/IntelligenceX/releases/tag/v1.2.0",
                  "publishedAt": "2026-02-25T10:00:00Z",
                  "isPrerelease": false,
                  "isLatestStable": true,
                  "body_md": "## Added\n- Stable improvements",
                  "assets": [
                    {
                      "name": "IntelligenceX.Chat-win-x64-v1.2.0.zip",
                      "downloadUrl": "https://example.test/downloads/ix-chat-win-x64-v1.2.0.zip",
                      "product": "intelligencex.chat",
                      "channel": "stable",
                      "platform": "windows",
                      "arch": "x64",
                      "kind": "zip",
                      "thumbnailUrl": "https://example.test/images/ix-chat-win-thumb.png",
                      "size": 5242880
                    },
                    {
                      "name": "IntelligenceX.Chat-linux-x64-v1.2.0.zip",
                      "downloadUrl": "https://example.test/downloads/ix-chat-linux-x64-v1.2.0.zip",
                      "product": "intelligencex.chat",
                      "channel": "stable",
                      "platform": "linux",
                      "arch": "x64",
                      "kind": "zip",
                      "size": 5021000
                    },
                    {
                      "name": "IntelligenceX.Docs-win-x64-v1.2.0.zip",
                      "downloadUrl": "https://example.test/downloads/ix-docs-win-x64-v1.2.0.zip",
                      "product": "intelligencex.docs",
                      "channel": "stable",
                      "platform": "windows",
                      "arch": "x64",
                      "kind": "zip",
                      "size": 3145728
                    }
                  ]
                },
                {
                  "tag": "v1.3.0-preview1",
                  "title": "IntelligenceX 1.3.0 Preview 1",
                  "url": "https://github.com/EvotecIT/IntelligenceX/releases/tag/v1.3.0-preview1",
                  "publishedAt": "2026-02-27T10:00:00Z",
                  "isPrerelease": true,
                  "isLatestPrerelease": true,
                  "body_md": "## Preview\n- Early bits",
                  "assets": [
                    {
                      "name": "IntelligenceX.Chat-win-x64-v1.3.0-preview1.zip",
                      "downloadUrl": "https://example.test/downloads/ix-chat-win-x64-v1.3.0-preview1.zip",
                      "product": "intelligencex.chat",
                      "channel": "preview",
                      "platform": "windows",
                      "arch": "x64",
                      "kind": "zip",
                      "size": 5400000
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static void WriteReleasePlacementData(string root)
    {
        var dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "release_placements.json"),
            """
            {
              "home": {
                "chat_primary": {
                  "product": "intelligencex.chat",
                  "channel": "stable",
                  "platform": "windows",
                  "arch": "x64",
                  "kind": "zip",
                  "label": "Get Chat (Placement)",
                  "class": "btn btn-primary"
                },
                "chat_matrix": {
                  "product": "intelligencex.chat",
                  "channel": "stable",
                  "groupBy": "platform",
                  "limit": 2
                }
              },
              "changelog": {
                "chat_timeline": {
                  "product": "intelligencex.chat",
                  "limit": 1,
                  "includePreview": false
                }
              }
            }
            """);
    }
}
