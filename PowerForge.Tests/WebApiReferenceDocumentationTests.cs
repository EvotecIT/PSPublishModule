using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiReferenceDocumentationTests
{
    [Fact]
    public void Generate_RecursivelyResolvesExplicitImportedTargetFromLoadedDependency()
    {
        WithRoot(root =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var namespaceName = "Imported.InheritDoc." + suffix;
            var dependency = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ImportedInheritDoc" + suffix), AssemblyBuilderAccess.Run);
            var module = dependency.DefineDynamicModule("ImportedInheritDoc" + suffix);
            var contract = module.DefineType(namespaceName + ".IContract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            var contractMethod = contract.DefineMethod("Other", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                typeof(void), Type.EmptyTypes);
            var contractType = contract.CreateType()!;
            var baseBuilder = module.DefineType(namespaceName + ".Base", TypeAttributes.Public | TypeAttributes.Class);
            baseBuilder.AddInterfaceImplementation(contractType);
            var baseMethod = baseBuilder.DefineMethod("Other", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void), Type.EmptyTypes);
            baseMethod.GetILGenerator().Emit(OpCodes.Ret);
            baseBuilder.DefineMethodOverride(baseMethod, contractMethod);
            var baseType = baseBuilder.CreateType()!;
            var targetId = "M:" + baseType.FullName + ".Other";
            var contractId = "M:" + contractType.FullName + ".Other";

            var assembly = Path.Combine(root, "PowerForge.Tests.dll");
            File.Copy(typeof(WebReferenceNamedDerived).Assembly.Location, assembly);
            var dependencyName = "ImportedInheritDoc" + suffix;
            File.WriteAllText(Path.Combine(root, dependencyName + ".dll"), "loaded from the test AppDomain");
            File.WriteAllText(Path.Combine(root, dependencyName + ".xml"), $"""
                <doc><members>
                  <member name="{targetId}"><inheritdoc/></member>
                  <member name="{contractId}"><summary>Imported interface contract summary.</summary></member>
                </members></doc>
                """);
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, $"""
                <doc><members>
                  <member name="T:PowerForge.Tests.WebReferenceNamedDerived"><summary>Derived.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceNamedDerived.Run"><inheritdoc cref="{targetId}"/></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = assembly, OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });

            Assert.Empty(result.Warnings);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "powerforge-tests-webreferencenamedderived.json")));
            var method = json.RootElement.GetProperty("methods").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "Run");
            Assert.Equal("Imported interface contract summary.", method.GetProperty("summary").GetString());
        });
    }

    [Fact]
    public void Generate_ResolvesExplicitOtherMemberFromAdjacentInheritedOwner()
    {
        WithRoot(root =>
        {
            var assembly = Path.Combine(root, "PowerForge.Tests.dll");
            File.Copy(typeof(WebReferenceNamedDerived).Assembly.Location, assembly);
            File.WriteAllText(Path.ChangeExtension(assembly, ".xml"), "<doc><members><member name=\"M:PowerForge.Tests.WebReferenceNamedBase.Other\"><summary>The explicitly selected contract.</summary></member></members></doc>");
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:PowerForge.Tests.WebReferenceNamedDerived"><summary>Derived.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceNamedDerived.Run"><inheritdoc cref="M:PowerForge.Tests.WebReferenceNamedBase.Other"/></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = assembly, OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "powerforge-tests-webreferencenamedderived.json")));
            var method = json.RootElement.GetProperty("methods").EnumerateArray().Single(method => method.GetProperty("name").GetString() == "Run");
            Assert.Equal("The explicitly selected contract.", method.GetProperty("summary").GetString());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_ResolvesNearestCollectionDocumentationAcrossFrameworkFiles(bool explicitTarget)
    {
        WithRoot(root =>
        {
            var xml = Path.Combine(root, "docs.xml");
            var inherit = explicitTarget ? "<inheritdoc cref=\"T:System.Collections.Generic.List`1\"/>" : "<inheritdoc/>";
            File.WriteAllText(xml, "<doc><members><member name=\"T:PowerForge.Tests.WebReferenceCollectionFixture\">" + inherit + "</member></members></doc>");
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = typeof(WebReferenceCollectionFixture).Assembly.Location,
                OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.Empty(result.Warnings);
            Assert.Equal(1, result.TypeCount);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "powerforge-tests-webreferencecollectionfixture.json")));
            Assert.Contains("list", json.RootElement.GetProperty("summary").GetString()!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pipeline_ReportsMalformedAssemblyAndWritesFailureProfileWithOrWithoutCaching(bool cache)
    {
        WithRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "bad.dll"), "Interrupted assembly output");
            File.WriteAllText(Path.Combine(root, "docs.xml"), "<doc><members/></doc>");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, "{\"cache\":" + (cache ? "true" : "false") + ",\"profilePath\":\"failure.json\",\"steps\":[{\"task\":\"apidocs\",\"xml\":\"docs.xml\",\"assembly\":\"bad.dll\",\"out\":\"api\",\"format\":\"json\"}]}");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            var step = Assert.Single(result.Steps);
            Assert.False(step.Cached);
            Assert.Contains("could not be inspected", step.Message);
            using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "failure.json")));
            Assert.False(profile.RootElement.GetProperty("success").GetBoolean());
        });
    }

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
    [InlineData(false, null, "assemblies")]
    [InlineData(false, null, "assemblyPaths")]
    [InlineData(false, "inputs", "assemblies")]
    [InlineData(false, "entries", "assemblyPaths")]
    public void PipelineCache_TracksInheritedDocumentationBytes(bool explicitInput, string? batchKey = null, string assemblyKey = "assembly")
    {
        WithRoot(root =>
        {
            var assembly = Path.Combine(root, "PowerForge.Tests.dll");
            File.Copy(typeof(WebInheritanceImplementation).Assembly.Location, assembly);
            var inherited = explicitInput ? Path.Combine(root, "contracts.xml") : Path.ChangeExtension(assembly, ".xml");
            string Contracts(string summary) => "<doc><assembly><name>PowerForge.Tests</name></assembly><members><member name=\"T:PowerForge.Tests.WebInheritanceBase\"><summary>Base.</summary></member>" +
                "<member name=\"M:PowerForge.Tests.WebInheritanceBase.Execute(System.String)\"><summary>" + summary + "</summary></member></members></doc>";
            File.WriteAllText(inherited, Contracts("Initial documentation"));
            File.WriteAllText(Path.Combine(root, "docs.xml"), """
                <doc><assembly><name>PowerForge.Tests</name></assembly><members>
                  <member name="T:PowerForge.Tests.WebInheritanceImplementation"><summary>Implementation.</summary></member>
                  <member name="M:PowerForge.Tests.WebInheritanceImplementation.Execute(System.String)"><inheritdoc/></member>
                </members></doc>
                """);
            var pipeline = Path.Combine(root, "pipeline.json");
            var assemblyInput = assemblyKey == "assembly" ? "\"PowerForge.Tests.dll\"" : "[\"PowerForge.Tests.dll\"]";
            var fields = "\"xml\":\"docs.xml\",\"" + assemblyKey + "\":" + assemblyInput + ",\"out\":\"api\",\"format\":\"json\",\"includeUndocumented\":false,\"failOnWarnings\":false" +
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

    [Theory]
    [InlineData("assembly")]
    [InlineData("assemblies")]
    [InlineData("assemblyPaths")]
    public void PipelineCache_ReleasesAssemblyFilesAndInspectsRebuiltBytesAtTheSamePath(string assemblyKey)
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
            File.WriteAllText(xml, "<doc><assembly><name>WebReferenceRefresh</name></assembly><members><member name=\"T:WebReferenceRefreshFixture\"><summary>Current API.</summary></member></members></doc>");
            var pipeline = Path.Combine(root, "pipeline.json");
            var step = new Dictionary<string, object?>
            {
                ["task"] = "apidocs", ["xml"] = "docs.xml", ["out"] = "api", ["format"] = "json",
                [assemblyKey] = assemblyKey == "assembly" ? (object)assemblyPath : new[] { assemblyPath }
            };
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new { cache = true, steps = new[] { step } }));
            WriteAssembly("BeforeRebuild");
            var first = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(first.Success, Assert.Single(first.Steps).Message);
            var output = Path.Combine(root, "api", "types", "webreferencerefreshfixture.json");
            Assert.Contains("BeforeRebuild", File.ReadAllText(output));
            Assert.True(Assert.Single(WebPipelineRunner.RunPipeline(pipeline, logger: null).Steps).Cached);
            var timestamp = File.GetLastWriteTimeUtc(assemblyPath);
            WriteAssembly("AfterRebuild");
            File.SetLastWriteTimeUtc(assemblyPath, timestamp);
            var rebuilt = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(rebuilt.Success, Assert.Single(rebuilt.Steps).Message);
            Assert.False(Assert.Single(rebuilt.Steps).Cached);
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

public sealed class WebReferenceCollectionFixture : List<int> { }

public class WebReferenceNamedBase
{
    public virtual void Run() { }
    public void Other() { }
}

public sealed class WebReferenceNamedDerived : WebReferenceNamedBase
{
    public override void Run() { }
}
