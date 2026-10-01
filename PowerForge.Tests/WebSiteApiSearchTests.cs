using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteApiSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-api-search-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "out");
    private readonly SiteSpec _spec;

    public WebSiteApiSearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(_root, "content", "guide.md"), "---\ntitle: Guide\nslug: guide\n---\nDocument guidance.");
        File.WriteAllText(Path.Combine(_root, "site.json"), "{}");
        _spec = new SiteSpec
        {
            Name = "Search test", BaseUrl = "https://example.test",
            Collections = [new CollectionSpec { Name = "docs", Input = "content", Output = "/" }],
            Search = new SearchSpec { ApiRoots = ["api"] }
        };
    }

    [Fact]
    public void Build_IncludesApiAndCommandsAsHtmlLinksAndRefreshesChangedCatalogs()
    {
        WriteCatalog("word", """[{"title":"WordDocument","slug":"worddocument","kind":"Class","summary":"Create Word files.","aliases":["DocumentFactory"]}]""", "worddocument");
        WriteCatalog("powershell", """[{"title":"New-OfficeWord","slug":"new-officeword","kind":"Cmdlet","summary":"Create a document."}]""", "new-officeword");
        Build();
        var entries = ReadIndex();
        Assert.Contains(entries, entry => entry.Url == "/guide");
        var word = Assert.Single(entries, entry => entry.Title == "WordDocument");
        Assert.Equal("/api/word/worddocument/", word.Url);
        Assert.Contains("DocumentFactory", word.SearchText);
        Assert.Equal("en", word.Language);
        Assert.Equal("powershell", Assert.Single(entries, entry => entry.Title == "New-OfficeWord").Collection);
        Assert.Equal(entries.Length, JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "search", "manifest.json"))).RootElement.GetProperty("entryCount").GetInt32());

        File.WriteAllText(Path.Combine(Output, "api", "word", "search.json"), "[]");
        Build();
        Assert.DoesNotContain(ReadIndex(), entry => entry.Title == "WordDocument");
        Assert.Single(ReadIndex(), entry => entry.Title == "New-OfficeWord");
    }

    [Fact]
    public void Build_OnlyIndexesExistingHtmlAndDeduplicatesOverlappingRoots()
    {
        WriteCatalog("word", """[{"title":"WordDocument","slug":"worddocument","kind":"Class"},{"title":"Missing","slug":"missing"},{"title":"Unsafe","slug":"../outside"}]""", "worddocument");
        _spec.Search!.ApiRoots = ["api", "api/word"];
        Build();
        Assert.Single(ReadIndex(), entry => entry.Collection == "api");
    }

    [Fact]
    public void Build_IndexesReferenceOnlySitesAndClearsRemovedReferences()
    {
        File.Delete(Path.Combine(_root, "content", "guide.md"));
        WriteCatalog("word", """[{"title":"WordDocument","slug":"worddocument","kind":"Class"}]""", "worddocument");
        Build();
        Assert.Equal("WordDocument", Assert.Single(ReadIndex()).Title);
        File.WriteAllText(Path.Combine(Output, "api", "word", "search.json"), "[]");
        Build();
        Assert.Empty(ReadIndex());
    }

    [Fact]
    public void Build_KeepsMemberFragmentsAndAllEntriesInBoundedQueryShards()
    {
        WriteCatalog("word", """[{"title":"WordDocument","slug":"worddocument","kind":"Class"}]""", "worddocument");
        var members = Enumerable.Range(0, 5100).Select(number => new
        {
            title = "WordDocument.SaveAsPdf", slug = "worddocument", anchor = "method-save-" + number,
            kind = "method", assembly = "Word.Pdf", signature = "SaveAsPdf(string path)",
            aliases = new[] { "SaveAsPdf" }, summary = "Export Word to PDF."
        });
        File.WriteAllText(Path.Combine(Output, "api", "word", "members.json"), JsonSerializer.Serialize(members));
        Build();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "search", "manifest.json")));
        var shards = manifest.RootElement.GetProperty("queryShards").EnumerateArray().ToArray();
        var all = shards.SelectMany(shard => JsonSerializer.Deserialize<SearchIndexEntry[]>(
            File.ReadAllText(Path.Combine(Output, shard.GetProperty("path").GetString()!.TrimStart('/'))),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!).ToArray();
        Assert.Equal(5102, all.Length);
        Assert.Equal(all.Length, all.Select(entry => entry.Url).Distinct().Count());
        Assert.All(shards, shard => Assert.False(shard.GetProperty("truncated").GetBoolean()));
        Assert.All(all.Where(entry => entry.Kind == "method"), entry =>
        {
            Assert.StartsWith("/api/word/worddocument/#method-save-", entry.Url);
            Assert.Equal("Word.Pdf", entry.Project);
            Assert.NotNull(entry.Meta);
        });
        var before = shards.Select(shard => shard.GetProperty("path").GetString()!).ToArray();
        var unrelated = Path.Combine(Output, "search", "query", "custom.json");
        File.WriteAllText(unrelated, "{}");
        File.WriteAllText(Path.Combine(Output, "api", "word", "members.json"), "[]");
        Build();
        using var refreshed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "search", "manifest.json")));
        var retained = refreshed.RootElement.GetProperty("queryShards").EnumerateArray().Select(shard => shard.GetProperty("path").GetString()!).ToHashSet();
        Assert.All(before.Except(retained), path => Assert.False(File.Exists(Path.Combine(Output, path.TrimStart('/')))));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Build_RejectsApiRootsOutsideTheOutput()
    {
        _spec.Search!.ApiRoots = ["../outside"];
        Assert.Throws<ArgumentException>(Build);
    }

    private void WriteCatalog(string name, string json, string slug)
    {
        var path = Path.Combine(Output, "api", name);
        Directory.CreateDirectory(Path.Combine(path, slug));
        File.WriteAllText(Path.Combine(path, slug, "index.html"), "<h1>Reference</h1>");
        File.WriteAllText(Path.Combine(path, "search.json"), json);
    }
    private void Build() => WebSiteBuilder.Build(_spec, WebSitePlanner.Plan(_spec, Path.Combine(_root, "site.json")), Output);
    private SearchIndexEntry[] ReadIndex() => JsonSerializer.Deserialize<SearchIndexEntry[]>(File.ReadAllText(Path.Combine(Output, "search", "index.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
