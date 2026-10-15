using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorSourceCoverageTests
{
    [Theory]
    [InlineData("all")]
    [InlineData("assembly")]
    public void SourceMemberScopeKeepsInheritedApiVisibleAndCountsMissingOwnedLinks(string scope)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-source-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "fixture.xml");
            File.WriteAllText(xml, """<doc><members><member name="T:PowerForge.Tests.ApiDocsSourceFixtures.SourceFixtureDerived"><summary>Derived.</summary></member></members></doc>""");
            var options = new WebApiDocsOptions
            {
                XmlPath = xml,
                AssemblyPath = typeof(PowerForge.Tests.ApiDocsSourceFixtures.SourceFixtureDerived).Assembly.Location,
                OutputPath = Path.Combine(root, "api"),
                Format = "json",
                IncludeUndocumentedTypes = false,
                SourceMemberCoverageScope = scope
                // No source URL: owned declarations must still count as missing.
            };
            var result = WebApiDocsGenerator.Generate(options);
            using var api = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "types", "powerforge-tests-apidocssourcefixtures-sourcefixturederived.json")));
            var methods = api.RootElement.GetProperty("methods").EnumerateArray().ToArray();
            Assert.Contains(methods, method => method.GetProperty("declaringType").GetString() == "System.Object");
            Assert.Contains(methods, method => method.GetProperty("name").GetString() == "Describe");
            using var report = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var coverage = report.RootElement.GetProperty("source").GetProperty("members");
            var total = report.RootElement.GetProperty("members").GetProperty("count").GetInt32();
            var excluded = methods.Count(method => method.GetProperty("declaringType").GetString() == "System.Object");
            Assert.Equal(scope == "all" ? 0 : excluded, coverage.GetProperty("excludedExternalMembers").GetInt32());
            Assert.Equal(scope == "all" ? total : total - excluded, coverage.GetProperty("count").GetInt32());
            Assert.Equal(0, coverage.GetProperty("url").GetProperty("covered").GetInt32());
            Assert.True(coverage.GetProperty("url").GetProperty("total").GetInt32() > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../../../../_/CodeGlyphX/Barcode.cs")]
    [InlineData("../../CodeGlyphX/CodeGlyphX/Barcode.cs")]
    public void GitHubRepoMismatchHint_IgnoresLeadingRelativePdbSegments(string sourcePath)
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(sourcePath, url, out var hint);

        Assert.False(mismatch);
        Assert.Equal(string.Empty, hint);
    }

    [Theory]
    [InlineData("../../WrongRepository/WrongRepository/Barcode.cs")]
    [InlineData("../../../../_/WrongRepository/WrongRepository/Barcode.cs")]
    public void GitHubRepoMismatchHint_StillDetectsWrongRepositoryAfterPortablePdbPrefixes(string sourcePath)
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(
            sourcePath,
            url,
            out var hint);

        Assert.True(mismatch);
        Assert.Equal("WrongRepository -> CodeGlyphX", hint);
    }
}
