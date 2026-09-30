using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests
{
    public sealed class WebApiMemberSearchTests
    {
        [Fact]
        public void Generate_MembersLinkToRenderedOverloadsAndExposeExtensionContext()
        {
            var root = Path.Combine(Path.GetTempPath(), "pf-member-search-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var xml = Path.Combine(root, "fixture.xml");
                File.WriteAllText(xml, """
                    <doc><assembly><name>PowerForge.Tests</name></assembly><members>
                    <member name="T:PowerForge.Tests.ApiSearchFixture.Document"><summary>Document model.</summary></member>
                    <member name="T:PowerForge.Tests.ApiSearchFixture.Converter"><summary>Conversion extensions.</summary></member>
                    </members></doc>
                    """);
                var options = new WebApiDocsOptions
                {
                    XmlPath = xml, AssemblyPath = typeof(ApiSearchFixture.Document).Assembly.Location, PackageId = "Document.Pdf",
                    OutputPath = Path.Combine(root, "api"), BaseUrl = "/api/fixture", Template = "docs", Format = "both"
                };
                options.IncludeNamespacePrefixes.Add("PowerForge.Tests.ApiSearchFixture");
                WebApiDocsGenerator.Generate(options);
                using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "members.json")));
                var entries = catalog.RootElement.EnumerateArray().ToArray();
                var saves = entries.Where(entry => entry.GetProperty("title").GetString() == "Converter.SaveAsPdf").ToArray();
                Assert.Equal(2, saves.Length);
                Assert.All(saves, entry =>
                {
                    Assert.Equal("extension", entry.GetProperty("kind").GetString());
                    Assert.Equal("Document.Pdf", entry.GetProperty("packageId").GetString());
                    Assert.Contains("Document", entry.GetProperty("receiverType").GetString());
                    Assert.Contains("SaveAsPdf", entry.GetProperty("signature").GetString());
                    Assert.Contains("CancellationToken cancellationToken = default", entry.GetProperty("signature").GetString());
                });
                Assert.Equal(saves.Length, saves.Select(entry => entry.GetProperty("url").GetString()).Distinct().Count());
                Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "property");
                Assert.Contains(entries, entry => entry.GetProperty("anchor").GetString()!.StartsWith("member-"));
                foreach (var entry in entries)
                {
                    var html = File.ReadAllText(Path.Combine(options.OutputPath, entry.GetProperty("slug").GetString()!, "index.html"));
                    Assert.Contains("id=\"" + entry.GetProperty("anchor").GetString() + "\"", html);
                }
                // Switching to the simple renderer must not leave docs-only member links.
                options.Template = "default";
                WebApiDocsGenerator.Generate(options);
                using var simpleCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "members.json")));
                Assert.Empty(simpleCatalog.RootElement.EnumerateArray());
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

namespace PowerForge.Tests.ApiSearchFixture
{
    public sealed class Document
    {
        public string Title { get; set; } = string.Empty;
    }
    public static class Converter
    {
        public static void SaveAsPdf(this Document document, string path, CancellationToken cancellationToken = default) { }
        public static void SaveAsPdf(this Document document, Stream stream, CancellationToken cancellationToken = default) { }
        public static void SaveAsHtml(this Document document,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Document>>> options,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Document>>> resources,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Document>>> metadata) { }
    }
}
