using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSeoDoctorLocalizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-web-seo-localization-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("pt-BR", "pt-PT", false)]
    [InlineData("zh-Hans", "zh-Hant", false)]
    [InlineData("da", "nb", false)]
    [InlineData("pt-BR", " PT-br ", true)]
    [InlineData("en", "en", true)]
    [InlineData("", "", true)]
    [InlineData("invalid language", "", true)]
    [InlineData("ja-JP-u-ca-japanese", "", false)]
    [InlineData("ja-JP-u-ca-japanese", "JA-jp-U-ca-JAPANESE", true)]
    [InlineData("zh-Hant-x-site", "ja-JP-x-site", false)]
    [InlineData("x-site-ja", "x-site-zh", false)]
    [InlineData("i-klingon", "", false)]
    public void DuplicateIntentRespectsDeclaredLanguage(string firstLanguage, string secondLanguage, bool duplicate)
    {
        WritePage("first", firstLanguage, "Shared translated product guide", new string('a', 80));
        WritePage("second", secondLanguage, "Shared translated product guide", new string('a', 80));

        var result = WebSeoDoctor.Analyze(Options());

        Assert.Equal(duplicate, result.Issues.Any(issue => issue.Hint == "duplicate-title-intent"));
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("ko-KR")]
    [InlineData("zh-Hans")]
    [InlineData("ZH-hant-TW")]
    [InlineData("ja-JP-u-ca-japanese")]
    [InlineData("zh-Hant-x-site")]
    [InlineData("ko-KR-x-a")]
    public void DenseLanguageDescriptionsUseTheSameMinimumInMetricsAndIssues(string language)
    {
        WritePage("guide", language, new string('家', 18), new string('家', 40));

        var result = WebSeoDoctor.Analyze(Options());

        Assert.DoesNotContain(result.Issues, issue => issue.Category is "title" or "description");
        Assert.False(Assert.Single(result.PageMetrics).ShortDescription);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("")]
    [InlineData("unknown-language-value")]
    public void OtherLanguagesKeepTheConfiguredMinimum(string language)
    {
        WritePage("guide", language, new string('a', 18), new string('a', 40));

        var result = WebSeoDoctor.Analyze(Options());

        Assert.Contains(result.Issues, issue => issue.Hint == "title-short");
        Assert.Contains(result.Issues, issue => issue.Hint == "description-short");
        Assert.True(Assert.Single(result.PageMetrics).ShortDescription);
    }

    [Fact]
    public void DenseLanguageStillChecksMissingShortAndLongMetadata()
    {
        WritePage("missing", "ja", "", "");
        WritePage("short", "ja", "家", "家");
        WritePage("long", "ja", new string('家', 61), new string('家', 161));

        var result = WebSeoDoctor.Analyze(Options());

        foreach (var hint in new[] { "title-missing", "description-missing", "title-short", "description-short", "title-long", "description-long" })
            Assert.Contains(result.Issues, issue => issue.Hint == hint);
    }

    [Fact]
    public void DenseLanguageScalesCustomMinimums()
    {
        WritePage("guide", "ja", new string('家', 18), new string('家', 40));
        var options = Options();
        options.MinTitleLength = 40;
        options.MinDescriptionLength = 100;

        var result = WebSeoDoctor.Analyze(options);

        Assert.Contains(result.Issues, issue => issue.Hint == "title-short");
        Assert.Contains(result.Issues, issue => issue.Hint == "description-short");
    }

    [Fact]
    public void DuplicateIntentBaselineKeysDistinguishLanguageAndUnicodeTitle()
    {
        WritePage("first-en", "en", "Shared translated product guide", new string('a', 80));
        WritePage("second-en", "en", "Shared translated product guide", new string('a', 80));
        var baselineKey = Assert.Single(WebSeoDoctor.Analyze(Options()).Issues,
            issue => issue.Hint == "duplicate-title-intent").Key;

        WritePage("first-nl", "nl", "Shared translated product guide", new string('a', 80));
        WritePage("second-nl", "nl", "Shared translated product guide", new string('a', 80));
        WritePage("first-ja", "ja", "プライバシー", new string('家', 80));
        WritePage("second-ja", "ja", "プライバシー", new string('家', 80));
        WritePage("third-ja", "ja", "サポート", new string('家', 80));
        WritePage("fourth-ja", "ja", "サポート", new string('家', 80));

        var keys = WebSeoDoctor.Analyze(Options()).Issues
            .Where(issue => issue.Hint == "duplicate-title-intent")
            .Select(issue => issue.Key).ToArray();

        Assert.Equal(4, keys.Length);
        Assert.Equal(4, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(keys, key => key == baselineKey);
    }

    private void WritePage(string route, string language, string title, string description)
    {
        var directory = Path.Combine(_root, route);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "index.html"), $"""
            <!doctype html><html lang="{language}"><head><title>{title}</title>
            <meta name="description" content="{description}"></head><body><h1>Guide</h1></body></html>
            """);
    }

    private WebSeoDoctorOptions Options() => new()
    {
        SiteRoot = _root,
        CheckOrphanPages = false,
        CheckCanonical = false,
        CheckHreflang = false,
        CheckStructuredData = false
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
