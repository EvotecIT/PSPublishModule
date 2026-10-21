using PowerForge.Web;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class WebSocialCardContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-social-content-" + Guid.NewGuid().ToString("N"));

    public WebSocialCardContentTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

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
