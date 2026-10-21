using PowerForge.Web;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class WebSocialCardContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-social-content-" + Guid.NewGuid().ToString("N"));

    public WebSocialCardContentTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task RemoteArtwork_UsesFallbackOrFailsWhenDisabledOrUnavailable_AndHonorsEnabledSources()
    {
        var port = LoopbackHttpListener.GetCandidatePort();
        using var listener = LoopbackHttpListener.Start(ref port);
        using var stop = new CancellationTokenSource();
        var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var request = await listener.GetContextAsync().WaitAsync(stop.Token);
                    Interlocked.Increment(ref requests);
                    var valid = request.Request.Url!.AbsolutePath == "/image.svg";
                    request.Response.StatusCode = valid ? 200 : 404;
                    request.Response.ContentType = "image/svg+xml";
                    var bytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><rect width=\"20\" height=\"20\" fill=\"red\"/></svg>");
                    request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes);
                    request.Response.Close();
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Net.HttpListenerException) when (stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            var remote = $"http://127.0.0.1:{port}/image.svg";
            var missing = $"http://127.0.0.1:{port}/missing.svg";
            var spec = Site();
            spec.Social!.GeneratedCardVariant = "split";
            spec.Social.GeneratedCardImagesRequired = true;
            spec.Social.GeneratedCardImages = new() { ["docs"] = new() { Pattern = "/unused.svg", Fallback = "/cards/fallback.svg" } };
            WriteSvg("cards/fallback.svg", "#0000ff");
            var page = Page();
            page.Meta["social_card_image"] = remote;
            var disabledSource = Plan(spec, page).InlineImageDataUri;
            Assert.StartsWith("data:image/", disabledSource);
            Assert.Contains("#0000ff", Decode(disabledSource));
            Assert.Equal(0, requests);
            spec.Social.GeneratedCardAllowRemoteMediaFetch = true;
            Assert.Equal(remote, Plan(spec, page).InlineImageDataUri);
            Assert.True(requests > 0);
            page.Meta["social_card_image"] = missing;
            Assert.Contains("#0000ff", Decode(Plan(spec, page).InlineImageDataUri));
            spec.Social.GeneratedCardImages["docs"].Fallback = missing;
            Assert.Throws<InvalidOperationException>(() => Plan(spec, page));
            spec.Social.GeneratedCardImages = null;
            Assert.Throws<InvalidOperationException>(() => Plan(spec, page));
            spec.Social.GeneratedCardImages = new() { ["docs"] = new() { Pattern = remote } };
            page.Meta.Remove("social_card_image");
            Assert.Equal(remote, Plan(spec, page).InlineImageDataUri);
            spec.Social.GeneratedCardAllowRemoteMediaFetch = false;
            spec.Social.GeneratedCardImages["docs"].Pattern = "/missing.svg";
            spec.Social.GeneratedCardImages["docs"].Fallback = remote;
            Assert.Throws<InvalidOperationException>(() => Plan(spec, page));
            spec.Social.GeneratedCardThemes = new() { ["remote"] = new SocialCardThemeSpec { AllowRemoteMediaFetch = true } };
            spec.Social.GeneratedCardTheme = "remote";
            Assert.Equal(remote, Plan(spec, page).InlineImageDataUri);
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            await server;
        }
    }

    [Theory]
    [InlineData("api", "Terminal", "Terminal: The print-once entry point.", "The print-once entry point.")]
    [InlineData("powershell", "Format-ConsoleTable", "Format-ConsoleTable: Formats pipeline objects.", "Formats pipeline objects.")]
    [InlineData("api", "Terminal", "TerminalWidth: The display width.", "TerminalWidth: The display width.")]
    public void ApiDescriptions_RemoveOnlyTheMatchingTitlePrefix(string collection, string title, string description, string expected)
    {
        var spec = Site();
        spec.Social!.GeneratedCardTitle = "page";
        spec.Social.GeneratedCardTextFit = true;
        var page = Page();
        page.Collection = collection;
        page.Title = title;
        page.Description = description;
        Assert.Equal(expected, Plan(spec, page).Description);
        spec.Social.GeneratedCardTextFit = false;
        var legacy = WebSiteBuilder.CreateSocialCardGenerationPlan(spec, page, title, description, "Example", _root)!;
        Assert.Equal(description, legacy.RenderOptions.Description);
    }

    [Fact]
    public void CardTitle_PageAndCollectionSources_PreserveSeoAndExplicitOverride()
    {
        var spec = Site();
        var page = Page();
        Assert.Equal("Terminal | Example", Plan(spec, page).Title);
        spec.Social!.GeneratedCardTitle = "page";
        Assert.Equal("Terminal", Plan(spec, page).Title);
        spec.Social.GeneratedCardTitlesByCollection = new() { ["DOCS"] = "seo" };
        Assert.Equal("Terminal | Example", Plan(spec, page).Title);
        page.Meta["social_title"] = "Custom social title";
        Assert.Equal("Custom social title", Plan(spec, page).Title);
        Assert.Equal("Terminal", page.Title);
    }

    [Fact]
    public void CardBadges_UseLongestSegmentPrefix_ThenCollection_WithFrontMatterPrecedence()
    {
        var spec = Site();
        spec.Social!.GeneratedCardBadges = new() { ["/docs/"] = "Documentation", ["/docs/terminal/"] = "Terminal docs", ["docs"] = "Reference", ["/doc/"] = "Wrong" };
        var page = Page();
        Assert.Equal("TERMINAL DOCS", Plan(spec, page).Badge);
        page.OutputPath = "/docs/other/";
        Assert.Equal("DOCUMENTATION", Plan(spec, page).Badge);
        page.OutputPath = "/docs-extra/";
        Assert.Equal("REFERENCE", Plan(spec, page).Badge);
        spec.Social.GeneratedCardBadgeKey = "group";
        page.Meta["group"] = "Rendering";
        Assert.Equal("RENDERING", Plan(spec, page).Badge);
        page.Meta["social_card_badge"] = "Explicit";
        Assert.Equal("EXPLICIT", Plan(spec, page).Badge);
    }

    [Fact]
    public void CardImages_ExpandMetadataAndSlug_UseFallback_ReportMissing_AndHonorExplicitImage()
    {
        var spec = Site();
        spec.Social!.GeneratedCardVariant = "split";
        spec.Social.GeneratedCardImages = new() { ["docs"] = new() { Pattern = "/cards/{meta.card}-{slug}.svg", Fallback = "/cards/fallback.svg" } };
        var page = Page();
        WriteSvg("cards/frame-terminal.svg", "#ff0000");
        WriteSvg("cards/fallback.svg", "#0000ff");
        page.Meta["card"] = "frame";
        Assert.Contains("#ff0000", Decode(Plan(spec, page).InlineImageDataUri));
        page.Meta["card"] = "missing";
        var warnings = new List<string>();
        using var listener = new SocialTraceListener(warnings);
        Assert.Contains("#0000ff", Decode(Plan(spec, page).InlineImageDataUri));
        Assert.Contains(warnings, warning => warning.Contains("missing-terminal.svg", StringComparison.Ordinal));
        page.Meta.Remove("card");
        Assert.Contains("#0000ff", Decode(Plan(spec, page).InlineImageDataUri));
        page.Meta["social_card_image"] = "/cards/frame-terminal.svg";
        Assert.Contains("#ff0000", Decode(Plan(spec, page).InlineImageDataUri));
        page.Meta["social_card_image"] = "/cards/missing-explicit.svg";
        Assert.Contains("#0000ff", Decode(Plan(spec, page).InlineImageDataUri));
        Assert.Contains(warnings, warning => warning.Contains("missing-explicit.svg", StringComparison.Ordinal));
    }

    [Fact]
    public void CardImages_MissingPatternAndFallback_FailOnlyWhenRequired()
    {
        var spec = Site();
        spec.Social!.GeneratedCardVariant = "split";
        spec.Social.GeneratedCardImages = new() { ["docs"] = new() { Pattern = "/missing/{meta.card}.svg", Fallback = "/also-missing.svg" } };
        Assert.Equal(string.Empty, Plan(spec, Page()).InlineImageDataUri);
        spec.Social.GeneratedCardImagesRequired = true;
        Assert.Contains("Required social card image", Assert.Throws<InvalidOperationException>(() => Plan(spec, Page())).Message);
        spec.Social.GeneratedCardImages = null;
        var page = Page();
        page.Meta["social_card_image"] = "/missing-explicit.svg";
        Assert.Contains("Required social card image", Assert.Throws<InvalidOperationException>(() => Plan(spec, page)).Message);
        page.Meta.Remove("social_card_image");
        page.Meta["social_image"] = "/missing-cover.svg";
        Assert.Contains("Required social card image", Assert.Throws<InvalidOperationException>(() => Plan(spec, page)).Message);
    }

    [Fact]
    public void CardDescription_ConciseModeUsesFirstSentence_AndExplicitDescriptionWins()
    {
        var spec = Site();
        var page = Page();
        Assert.Equal("Use .NET 10.0 today. Then add a widget.", Plan(spec, page).Description);
        spec.Social!.GeneratedCardTextFit = true;
        Assert.Equal("Use .NET 10.0 today.", Plan(spec, page).Description);
        page.Description = "The authored page description. More detail follows.";
        Assert.Equal("The authored page description.", Plan(spec, page).Description);
        page.Meta["social_description"] = "A deliberate short description. Both sentences stay.";
        Assert.Equal("A deliberate short description. Both sentences stay.", Plan(spec, page).Description);
    }

    [Fact]
    public void CardDefaults_KeepRouteAndSvgIdentical_WhenNewOptionsAreUnset()
    {
        var spec = Site();
        var page = Page();
        var first = WebSiteBuilder.CreateSocialCardGenerationPlan(spec, page, "Terminal | Example", "Description", "Example", _root)!;
        Assert.Equal(OperatingSystem.IsLinux()
            ? "assets/social/generated/docs-terminal-608e28956a.png"
            : "assets/social/generated/docs-terminal-15010081e3.png", first.RelativePath);
        var firstSvg = WebSocialCardGenerator.RenderSvg(first.RenderOptions);
        var roundTrip = JsonSerializer.Deserialize<SiteSpec>(JsonSerializer.Serialize(spec))!;
        var second = WebSiteBuilder.CreateSocialCardGenerationPlan(roundTrip, page, "Terminal | Example", "Description", "Example", _root)!;
        Assert.Equal(first.RelativePath, second.RelativePath);
        Assert.Equal(firstSvg, WebSocialCardGenerator.RenderSvg(second.RenderOptions));
        Assert.False(second.RenderOptions.TextFit);
        Assert.Empty(second.RenderOptions.Fonts!);
    }

    [Fact]
    public void RequiredCards_FailOnUnrenderableCard_OptionalCardsReportAndReturnFallback()
    {
        var spec = Site();
        var page = Page();
        // XML 1.0 cannot represent this control character. Exercise the real failed SVG renderer boundary.
        page.Title = "Invalid\u0001title";
        spec.Social!.GeneratedCardTitle = "page";
        var warnings = new List<string>();
        using var listener = new SocialTraceListener(warnings);
        Assert.Equal(string.Empty, WebSiteBuilder.TryGenerateSocialCardPath(spec, page, _root, page.Title, "Description", "Example"));
        Assert.Contains(warnings, warning => warning.Contains("not rendered", StringComparison.Ordinal));
        spec.Social.SocialCardsRequired = true;
        Assert.Contains("not rendered", Assert.Throws<InvalidOperationException>(() =>
            WebSiteBuilder.TryGenerateSocialCardPath(spec, page, _root, page.Title, "Description", "Example")).Message);
    }

    [Theory]
    [InlineData("malformed-svg")]
    [InlineData("missing-size")]
    [InlineData("extreme-cover")]
    [InlineData("corrupt-raster")]
    public void SplitArtworkFailures_RespectOptionalAndRequiredCardPolicy(string scenario)
    {
        if (!WebSocialCardGenerator.IsPngRenderingAvailable()) return;
        var spec = Site();
        spec.Social!.GeneratedCardVariant = "split";
        var page = Page();
        var artwork = scenario switch
        {
            "malformed-svg" => "<svg",
            "missing-size" => "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"20\" height=\"20\"/></svg>",
            "extreme-cover" => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"200000\"><rect width=\"1\" height=\"200000\"/></svg>",
            _ => "not a PNG"
        };
        var mime = scenario == "corrupt-raster" ? "image/png" : "image/svg+xml";
        page.Meta["social_card_image"] = $"data:{mime};base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(artwork));
        var warnings = new List<string>();
        using var listener = new SocialTraceListener(warnings);
        Assert.Equal(string.Empty, WebSiteBuilder.TryGenerateSocialCardPath(spec, page, _root, page.Title, "Description", "Example"));
        Assert.Contains(warnings, warning => warning.Contains("not rendered", StringComparison.Ordinal));
        spec.Social.SocialCardsRequired = true;
        Assert.Contains("not rendered", Assert.Throws<InvalidOperationException>(() =>
            WebSiteBuilder.TryGenerateSocialCardPath(spec, page, _root, page.Title, "Description", "Example")).Message);
    }

    private WebSocialCardGenerator.SocialCardRenderOptions Plan(SiteSpec spec, ContentItem page)
        => WebSiteBuilder.CreateSocialCardGenerationPlan(spec, page, "Terminal | Example", "Use .NET 10.0 today. Then add a widget.", "Example", _root)!.RenderOptions;

    private static SiteSpec Site() => new()
    {
        Name = "Example", BaseUrl = "https://example.test", Social = new SocialSpec { AutoGenerateCards = true, GeneratedCardVariant = "panel" }
    };

    private ContentItem Page() => new()
    {
        Title = "Terminal", Slug = "terminal", OutputPath = "/docs/terminal/", Collection = "docs",
        SourcePath = Path.Combine(_root, "content", "terminal.md"), Meta = new()
    };

    private void WriteSvg(string relative, string color)
    {
        var path = Path.Combine(_root, "static", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><rect width=\"200\" height=\"100\" fill=\"{color}\"/></svg>");
    }

    private static string Decode(string? source) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(source![(source!.IndexOf(',') + 1)..]));

    private sealed class SocialTraceListener : System.Diagnostics.TraceListener, IDisposable
    {
        private readonly List<string> _messages;
        public SocialTraceListener(List<string> messages) { _messages = messages; System.Diagnostics.Trace.Listeners.Add(this); }
        public override void Write(string? message) { if (message is not null) lock (_messages) _messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
        protected override void Dispose(bool disposing) { System.Diagnostics.Trace.Listeners.Remove(this); base.Dispose(disposing); }
    }
}
