using System.Text.Json;
using PowerForge.Web;

public sealed class WebApiDocsGeneratorFidelityTests
{
    [Fact]
    public void Generate_PreservesNullableContractsAndMappedSourcePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-fidelity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "fixture.xml");
            File.WriteAllText(xml, """
                <doc><members>
                <member name="T:PowerForge.Tests.DocsFidelity.Fixture"><summary>A document fixture.</summary></member>
                <member name="M:PowerForge.Tests.DocsFidelity.Fixture.Create(System.String)"><summary>Create.</summary><param name="arg1">Provided input.</param></member>
                <member name="M:PowerForge.Tests.DocsFidelity.Fixture.Open(PowerForge.Tests.DocsFidelity.Fixture)"><summary>Open.</summary></member>
                </members></doc>
                """);
            var options = new WebApiDocsOptions
            {
                XmlPath = xml,
                AssemblyPath = typeof(PowerForge.Tests.DocsFidelity.Fixture).Assembly.Location,
                SourceRootPath = root,
                SourceUrlPattern = "https://example.invalid/repo/blob/main/{path}#L{line}",
                OutputPath = Path.Combine(root, "api"),
                Format = "both",
                Template = "docs",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = false
            };
            WebApiDocsGenerator.Generate(options);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath,
                "types", "powerforge-tests-docsfidelity-fixture.json")));
            var type = json.RootElement;
            Assert.Equal("Fixtures/DocsFidelityFixture.cs", type.GetProperty("source").GetProperty("path").GetString());
            Assert.StartsWith("https://example.invalid/repo/blob/main/Fixtures/DocsFidelityFixture.cs#L",
                type.GetProperty("source").GetProperty("url").GetString());
            var method = type.GetProperty("methods").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Create");
            Assert.Contains("String? Create(String? text = null)", method.GetProperty("signature").GetString());
            Assert.Equal("System.String?", method.GetProperty("parameters")[0].GetProperty("type").GetString());
            Assert.Equal("text", method.GetProperty("parameters")[0].GetProperty("name").GetString());
            var html = File.ReadAllText(Path.Combine(options.OutputPath, "powerforge-tests-docsfidelity-fixture", "index.html"));
            Assert.Contains("id=\"method-open-powerforge-tests-docsfidelity-fixture\"", html);
            var properties = type.GetProperty("properties").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
            Assert.Contains("String?[,]?", properties["Matrix"].GetProperty("signature").GetString());
            Assert.Contains("Dictionary<String, List<String?>?>?", properties["Values"].GetProperty("signature").GetString());
            Assert.Contains("String? name = null", type.GetProperty("constructors")[0].GetProperty("signature").GetString());
            Assert.Contains("String?", type.GetProperty("fields")[0].GetProperty("signature").GetString());
            Assert.Contains("Action<String?>?", type.GetProperty("events")[0].GetProperty("signature").GetString());
            Assert.Equal(JsonValueKind.Null, type.GetProperty("methods").EnumerateArray()
                .Single(m => m.GetProperty("name").GetString() == "Generated").GetProperty("source").ValueKind);
            Assert.All(type.GetProperty("methods").EnumerateArray()
                .Where(m => m.GetProperty("isInherited").GetBoolean()),
                inherited => Assert.Equal(JsonValueKind.Null, inherited.GetProperty("source").ValueKind));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Generate_MergesPartialDocumentationAndReportsConflictingSummaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-partials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "fixture.xml");
            File.WriteAllText(xml, """
                <doc><members>
                <member name="T:Example.Document"><summary>Creates and edits documents.</summary></member>
                <member name="T:Example.Document"><summary>Contains default settings.</summary><remarks>Supports streams.</remarks><example><code>Document.Create();</code></example></member>
                <member name="M:Example.Document.Create"><summary>Creates a document.</summary></member>
                <member name="M:Example.Document.Create"><summary>Creates a document.</summary><returns>A new document.</returns></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            { XmlPath = xml, OutputPath = output, Format = "json", IncludeUndocumentedTypes = false });
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "example-document.json")));
            Assert.Equal("Creates and edits documents.", json.RootElement.GetProperty("summary").GetString());
            Assert.Equal("Supports streams.", json.RootElement.GetProperty("remarks").GetString());
            Assert.Single(json.RootElement.GetProperty("examples").EnumerateArray());
            var method = Assert.Single(json.RootElement.GetProperty("methods").EnumerateArray());
            Assert.Equal("A new document.", method.GetProperty("returns").GetString());
            Assert.Contains(result.Warnings, warning => warning.Contains("conflicting summaries for 'T:Example.Document'"));
        }
        finally { Directory.Delete(root, true); }
    }
}
