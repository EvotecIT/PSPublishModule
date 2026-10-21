using System.Net;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsDescriptionBoundaryTests
{
    [Theory]
    [InlineData(ApiDocsType.CSharp, "docs", true, false)]
    [InlineData(ApiDocsType.CSharp, "simple", true, false)]
    [InlineData(ApiDocsType.PowerShell, "docs", true, false)]
    [InlineData(ApiDocsType.PowerShell, "simple", true, false)]
    [InlineData(ApiDocsType.CSharp, "docs", false, false)]
    [InlineData(ApiDocsType.CSharp, "simple", false, false)]
    [InlineData(ApiDocsType.PowerShell, "docs", false, false)]
    [InlineData(ApiDocsType.PowerShell, "simple", false, false)]
    [InlineData(ApiDocsType.CSharp, "docs", true, true)]
    [InlineData(ApiDocsType.PowerShell, "simple", true, true)]
    public void Generate_LongSummariesEndCleanlyInEveryDescriptionTag(ApiDocsType source, string template, bool hasSentences, bool quotedSentence)
    {
        const string sentences = "Formats pipeline objects as a width-aware table. Preserves explicit column widths and readable terminal output.";
        const string wordPrefix = "Formats pipeline objects as a width-aware table while preserving column widths, terminal colors, readable headings, and predictable pipeline output";
        var completeSentences = quotedSentence ? $"\"{sentences}\"" : sentences;
        var summary = hasSentences
            ? completeSentences + " Additional reference guidance explains configuration, formatting, pipeline handling, and all available environment settings."
            : wordPrefix + " in the configured terminal environment and every supported pipeline invocation";
        var expected = hasSentences ? completeSentences : wordPrefix + "…";
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-description-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "help.xml");
            File.WriteAllText(input, source == ApiDocsType.CSharp
                ? $"<doc><members><member name=\"T:Sample.Terminal\"><summary>{summary}</summary></member></members></doc>"
                : $$"""
                  <helpItems xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                    <command:command><command:details><command:name>Format-ConsoleTable</command:name><maml:description><maml:para>{{summary}}</maml:para></maml:description></command:details></command:command>
                  </helpItems>
                  """);
            var output = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions { Type = source, XmlPath = input, HelpPath = input,
                OutputPath = output, Title = "Sample Reference", BaseUrl = "/api", Format = "html", Template = template });
            var slug = source == ApiDocsType.CSharp ? "sample-terminal" : "format-consoletable";
            var path = template == "simple" ? Path.Combine(output, "types", slug + ".html") : Path.Combine(output, slug, "index.html");
            var html = File.ReadAllText(path);
            foreach (var tag in new[] { "description", "og:description", "twitter:description" })
            {
                var match = Regex.Match(html, $"<meta\\s+(?:name|property)=\"{Regex.Escape(tag)}\"\\s+content=\"([^\"]*)\"");
                Assert.True(match.Success, $"Expected {tag} in generated HTML.");
                Assert.Equal(expected, WebUtility.HtmlDecode(match.Groups[1].Value));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(" in the")]
    [InlineData(" on a")]
    [InlineData(" for an")]
    [InlineData(" with")]
    [InlineData(" and")]
    [InlineData(",")]
    [InlineData(":")]
    public void FitApiSeoDescription_WordFallbackRemovesDanglingWordsAndPunctuation(string ending)
    {
        var prefix = new string('x', 140) + " output";
        Assert.Equal(prefix + "…", Fit(prefix + ending + " " + new string('y', 100)));
        if (ending is "," or ":")
            Assert.Equal(prefix, Fit(prefix + ending));
    }

    [Fact]
    public void FitApiSeoDescription_PreservesHyphenatedWordsInFallback()
    {
        var prefix = new string('x', 140) + " built-in";
        Assert.Equal(prefix + "…", Fit(prefix + " " + new string('y', 100)));
    }

    [Theory]
    [InlineData("e.g.")]
    [InlineData("i.e.")]
    [InlineData("etc.")]
    [InlineData("Dr.")]
    [InlineData("U.S.")]
    public void FitApiSeoDescription_AbbreviationsDoNotEndASentence(string abbreviation)
    {
        var prefix = "Use " + abbreviation + " " + new string('x', 120) + " output";
        Assert.Equal(prefix + "…", Fit(prefix + " " + new string('y', 100)));
    }

    [Fact]
    public void FitApiSeoDescription_LeadingArticleDoesNotHideAnOversizedToken()
    {
        var token = new string('界', 158) + "😀tail";
        Assert.Equal(new string('界', 158) + "…", Fit("The " + token));
    }

    [Fact]
    public void FitApiSeoDescription_KeepsASentenceEndingExactlyAtTheLimit()
    {
        var sentence = "Documents " + new string('x', 149) + ".";
        Assert.Equal(160, sentence.Length);
        Assert.Equal(sentence, Fit(sentence + " Additional guidance extends beyond the metadata limit."));
    }

    [Theory]
    [InlineData("\"", "\"")]
    [InlineData("“", "”")]
    [InlineData("(", ")")]
    [InlineData("(\"", "\")")]
    public void FitApiSeoDescription_IncludesClosingDelimitersInTheSentence(string opening, string closing)
    {
        var sentence = opening + "The terminal preserves configured formatting." + closing;
        Assert.Equal(sentence, Fit(sentence + " Additional reference guidance explains configuration, formatting, pipeline handling, and all available environment settings."));
    }

    [Fact]
    public void FitApiSeoDescription_CountsClosingDelimitersWithinTheLimit()
    {
        var sentence = "\"" + new string('x', 157) + ".\"";
        Assert.Equal(160, sentence.Length);
        Assert.Equal(sentence, Fit(sentence + " Additional guidance extends beyond the metadata limit."));
    }

    [Fact]
    public void FitApiSeoDescription_ClosingDelimiterBeyondTheLimitRequiresFallback()
    {
        var sentence = "\"" + new string('x', 158) + ".\"";
        Assert.Equal(161, sentence.Length);
        Assert.Equal("\"" + new string('x', 158) + "…", Fit(sentence + " Additional guidance extends beyond the metadata limit."));
    }

    [Fact]
    public void FitApiSeoDescription_ClosingDelimiterDoesNotTurnAnAbbreviationIntoASentence()
    {
        var prefix = "Use (e.g.) " + new string('x', 120) + " output";
        Assert.Equal(prefix + "…", Fit(prefix + " " + new string('y', 100)));
    }

    private static string Fit(string value)
    {
        var method = typeof(WebApiDocsGenerator).GetMethod("FitApiSeoDescription",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [value]));
    }
}
