using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiReferenceDocumentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_ResolvesFrameworkInheritanceWithoutAddingFrameworkPages(bool explicitTargets)
    {
        WithRoot(root =>
        {
            var xml = Path.Combine(root, "docs.xml");
            var documentation = """
                <doc><members>
                  <member name="T:PowerForge.Tests.WebReferenceDocumentationFixture"><summary>Fixture.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceDocumentationFixture.Dispose"><inheritdoc/></member>
                  <member name="M:PowerForge.Tests.WebReferenceDocumentationFixture.ToString"><inheritdoc/></member>
                </members></doc>
                """;
            if (explicitTargets)
                documentation = documentation.Replace(".Dispose\"><inheritdoc/>", ".Dispose\"><inheritdoc cref=\"M:System.IDisposable.Dispose\"/>", StringComparison.Ordinal)
                    .Replace(".ToString\"><inheritdoc/>", ".ToString\"><inheritdoc cref=\"M:System.Object.ToString\"/>", StringComparison.Ordinal);
            File.WriteAllText(xml, documentation);
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = typeof(WebReferenceDocumentationFixture).Assembly.Location,
                OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Implicit inheritdoc", StringComparison.Ordinal));
            Assert.Equal(1, result.TypeCount);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "powerforge-tests-webreferencedocumentationfixture.json")));
            var methods = json.RootElement.GetProperty("methods").EnumerateArray()
                .Where(method => method.GetProperty("name").GetString() is "Dispose" or "ToString").ToArray();
            Assert.Equal(2, methods.Length);
            foreach (var method in methods)
                Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("summary").GetString()), method.GetRawText());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, "entries")]
    public void PipelineCache_TracksInheritedDocumentationBytes(bool explicitInput, string? batchKey = null)
    {
        WithRoot(root =>
        {
            var assembly = Path.Combine(root, "PowerForge.Tests.dll");
            File.Copy(typeof(WebInheritanceImplementation).Assembly.Location, assembly);
            var inherited = explicitInput ? Path.Combine(root, "contracts.xml") : Path.ChangeExtension(assembly, ".xml");
            string Contracts(string summary) => "<doc><members><member name=\"T:PowerForge.Tests.WebInheritanceBase\"><summary>Base.</summary></member>" +
                "<member name=\"M:PowerForge.Tests.WebInheritanceBase.Execute(System.String)\"><summary>" + summary + "</summary></member></members></doc>";
            File.WriteAllText(inherited, Contracts("Initial documentation"));
            File.WriteAllText(Path.Combine(root, "docs.xml"), """
                <doc><members>
                  <member name="T:PowerForge.Tests.WebInheritanceImplementation"><summary>Implementation.</summary></member>
                  <member name="M:PowerForge.Tests.WebInheritanceImplementation.Execute(System.String)"><inheritdoc/></member>
                </members></doc>
                """);
            var pipeline = Path.Combine(root, "pipeline.json");
            var fields = "\"xml\":\"docs.xml\",\"assembly\":\"PowerForge.Tests.dll\",\"out\":\"api\",\"format\":\"json\",\"includeUndocumented\":false,\"failOnWarnings\":false" +
                (explicitInput ? ",\"xmls\":[\"contracts.xml\"]" : "");
            var step = batchKey is null ? "{\"task\":\"apidocs\"," + fields + "}"
                : "{\"task\":\"apidocs\",\"" + batchKey + "\":[{" + fields + "}]}";
            File.WriteAllText(pipeline, "{\"cache\":true,\"steps\":[" + step + "]}");
            var first = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(first.Success, Assert.Single(first.Steps).Message);
            var output = Path.Combine(root, "api", "types", "powerforge-tests-webinheritanceimplementation.json");
            Assert.Contains("Initial documentation", File.ReadAllText(output));
            var second = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(Assert.Single(second.Steps).Cached);
            var timestamp = File.GetLastWriteTimeUtc(inherited);
            File.WriteAllText(inherited, Contracts("Changed documentation"));
            File.SetLastWriteTimeUtc(inherited, timestamp);
            var third = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(third.Success, Assert.Single(third.Steps).Message);
            Assert.False(Assert.Single(third.Steps).Cached);
            Assert.Contains("Changed documentation", File.ReadAllText(output));
            Assert.DoesNotContain("Initial documentation", File.ReadAllText(output));
            if (!explicitInput)
                Assert.False(File.Exists(Path.Combine(root, "api", "types", "powerforge-tests-webinheritancebase.json")));
        });
    }

    [Fact]
    public void Generate_ReleasesAssemblyFilesAndInspectsRebuiltBytesAtTheSamePath()
    {
        WithRoot(root =>
        {
            var assemblyPath = Path.Combine(root, "WebReferenceRefresh.dll");
            void WriteAssembly(string methodName)
            {
                var builder = new PersistedAssemblyBuilder(new AssemblyName("WebReferenceRefresh"), typeof(object).Assembly);
                var module = builder.DefineDynamicModule("Main");
                var type = module.DefineType("WebReferenceRefreshFixture", TypeAttributes.Public);
                type.DefineMethod(methodName, MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
                type.CreateType();
                builder.Save(assemblyPath);
            }
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><members><member name=\"T:WebReferenceRefreshFixture\"><summary>Current API.</summary></member></members></doc>");
            var options = new WebApiDocsOptions { AssemblyPath = assemblyPath, XmlPath = xml, OutputPath = Path.Combine(root, "api"), Format = "json" };
            WriteAssembly("BeforeRebuild");
            WebApiDocsGenerator.Generate(options);
            var output = Path.Combine(root, "api", "types", "webreferencerefreshfixture.json");
            Assert.Contains("BeforeRebuild", File.ReadAllText(output));
            WriteAssembly("AfterRebuild");
            WebApiDocsGenerator.Generate(options);
            Assert.Contains("AfterRebuild", File.ReadAllText(output));
            Assert.DoesNotContain("BeforeRebuild", File.ReadAllText(output));
        });
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-reference-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Generate_ReportsInheritDocWithoutAnInheritedContract()
    {
        WithRoot(root =>
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:PowerForge.Tests.WebReferenceDocumentationFixture"><summary>Fixture.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceDocumentationFixture.Remove"><inheritdoc/></member>
                </members></doc>
                """);
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = typeof(WebReferenceDocumentationFixture).Assembly.Location,
                OutputPath = Path.Combine(root, "api"), Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.Contains(result.Warnings, warning => warning.Contains("Implicit inheritdoc could not be resolved for M:PowerForge.Tests.WebReferenceDocumentationFixture.Remove", StringComparison.Ordinal));
        });
    }
}

public sealed class WebReferenceDocumentationFixture : IDisposable
{
    public void Dispose() { }
    public void Remove() { }
    public override string ToString() => "Fixture";
}
