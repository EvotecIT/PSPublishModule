using System.Text.Json;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebVisualStoryHtmlAndCacheTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Theory]
    [InlineData("<script src=\"https://example.test/app.js\"></script>")]
    [InlineData("<img src=\"../frame.png\" alt=\"Frame\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://example.test/app.css\">")]
    [InlineData("<img src=\"missing.png\" alt=\"Frame\">")]
    [InlineData("<svg><image href=\"https://example.test/frame.png\"></image></svg>")]
    [InlineData("<svg><filter><feImage href=\"https://example.test/frame.png\"></feImage></filter></svg>")]
    [InlineData("<svg><a id=\"target\"></a><set href=\"#target\" attributeName=\"href\" to=\"javascript:alert(1)\" dur=\"1s\"></set></svg>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://example.test\">")]
    [InlineData("<script>document.body.textContent = 'owned'</script>")]
    [InlineData("<img src=\"demo.png\" onload=\"document.body.textContent = 'owned'\">")]
    [InlineData("<iframe src=\"demo.html\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">Open</a>")]
    [InlineData("<svg xmlns:xlink=\"http://www.w3.org/1999/xlink\"><a xlink:href=\"javascript:alert(1)\">Open</a></svg>")]
    [InlineData("<img srcset=\"data:image/png;base64,AAAA 1x, https://example.test/frame.png 2x\">")]
    [InlineData("<img srcset=\"data:image/png;base64,AAAA, https://example.test/frame.png 2x\">")]
    [InlineData("<link rel=\"preload\" as=\"image\" imagesrcset=\"demo.png 1x, https://example.test/frame.png 2x\">")]
    [InlineData("<div style=\"background-image: \\75rl('https://example.test/frame.png')\"></div>")]
    [InlineData("<div style=\"background-image:image-set('https://example.test/frame.png' 1x)\"></div>")]
    [InlineData("<style>@import url(data:text/css,body%7Bbackground:url(https://example.test/frame.png)%7D);</style>")]
    [InlineData("<svg><rect fill=\"url(https://example.test/fill.svg#paint)\"></rect></svg>")]
    [InlineData("<a href=\"data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20onload='alert(1)'/%3E\">Open</a>")]
    [InlineData("<svg><defs><linearGradient href=\"https://example.test/gradient.svg#fill\"/></defs></svg>")]
    [InlineData("<svg><defs><pattern href=\"../pattern.svg#fill\"/></defs></svg>")]
    [InlineData("<a href=\"java&#x0A;script:alert(1)\">Open</a>")]
    [InlineData("<link rel=\"prefetch\" href=\"https://example.test/next.html\">")]
    [InlineData("<link rel=\"prerender\" href=\"https://example.test/next.html\">")]
    [InlineData("<link rel=\"mask-icon\" href=\"https://example.test/mask.svg\">")]
    [InlineData("<link rel=\"apple-touch-icon\" href=\"https://example.test/icon.png\">")]
    [InlineData("<body background=\"https://example.test/wallpaper.png\"></body>")]
    public void Stage_RejectsHtmlDependenciesOutsideDeclaredBundleArtifacts(string html)
    {
        var root = CreateBundle(html);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = Path.Combine(root, "source", "story.json"),
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("self-contained", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root, "published", "visual-story.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Stage_AllowsDataAndDeclaredCandidatesInSourceSets()
    {
        var root = CreateBundle(
            "<link rel=\"preload\" as=\"image\" imagesrcset=\"data:image/png;base64,AAAA 1x, demo.png 2x\">" +
            "<img srcset=\"data:image/png;base64,AAAA 1x, demo.png 2x\" alt=\"Completed result\">");
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Contains(result.Bundle.Artifacts, artifact => artifact.Format == "html");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Stage_AllowsSelfContainedDataUrlsInCssUrlFunctions()
    {
        var root = CreateBundle(
            "<div style=\"background-image:url(data:image/png;base64,AAAA)\"></div>" +
            "<style>body{background-image:url('data:image/png;base64,AAAA')}</style>");
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Contains(result.Bundle.Artifacts, artifact => artifact.Format == "html");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Stage_AllowsHtmlDependenciesThatResolveToDeclaredArtifacts()
    {
        var root = CreateBundle("<img src=\"demo.png?version=1#result\" alt=\"Completed result\">");
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });

            var loaded = WebVisualStoryStager.Load(result.ManifestPath);
            Assert.Contains(loaded.Artifacts, artifact => artifact.Format == "html");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Shortcode_ReusesValidatedBundleUntilAFileChanges()
    {
        var root = CreateBundle("<img src=\"demo.png\" alt=\"Completed result\">");
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });
            var loadCount = 0;
            var cache = new WebVisualStoryBundleCache(path =>
            {
                loadCount++;
                return WebVisualStoryStager.Load(path);
            });
            var context = new ShortcodeRenderContext
            {
                RootPath = root,
                VisualStoryCache = cache
            };
            const string shortcode = "{{< story manifest=\"published/visual-story.json\" base=\"/story\" transcript=\"hidden\" >}}";

            _ = ShortcodeProcessor.Apply(shortcode, context);
            _ = ShortcodeProcessor.Apply(shortcode, context);

            Assert.Equal(1, loadCount);

            var htmlPath = Path.Combine(root, "published", "demo.html");
            File.AppendAllText(htmlPath, " ");
            Assert.Throws<InvalidOperationException>(() => ShortcodeProcessor.Apply(shortcode, context));
            Assert.Equal(2, loadCount);
            Assert.Equal(Path.Combine(root, "published", "visual-story.json"), result.ManifestPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateBundle(string html)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-story-html-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "demo.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes pulse{from{opacity:.5}to{opacity:1}}#box{animation:pulse 1s infinite}</style><rect id=\"box\" width=\"2\" height=\"2\"/></svg>");
        using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
            completed.Write(Path.Combine(source, "demo.png"), MagickFormat.Png);
        File.WriteAllText(Path.Combine(source, "demo.html"), html);

        var bundle = new WebVisualStoryBundle
        {
            SchemaVersion = 1,
            Id = "html-story",
            Title = "HTML story",
            Alt = "The completed result.",
            Outcome = "The result is visible.",
            Artifacts =
            [
                new WebVisualStoryArtifact { Role = "animated", Format = "svg", Path = "demo.svg" },
                new WebVisualStoryArtifact { Role = "completed", Format = "png", Path = "demo.png" },
                new WebVisualStoryArtifact { Role = "html", Format = "html", Path = "demo.html" }
            ]
        };
        File.WriteAllText(
            Path.Combine(source, "story.json"),
            JsonSerializer.Serialize(bundle, JsonOptions));
        return root;
    }
}
