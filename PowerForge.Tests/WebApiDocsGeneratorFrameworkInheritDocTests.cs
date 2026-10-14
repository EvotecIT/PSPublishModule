using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorFrameworkInheritDocTests
{
    [Fact]
    public void Pipeline_ResolvesFrameworkXmlWithOnlyPluralAssemblyInputs()
    {
        WithRoot(root =>
        {
            var assembly = typeof(WebReferenceDocumentationFixture).Assembly.Location;
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, $"""
                <doc><assembly><name>{typeof(WebReferenceDocumentationFixture).Assembly.GetName().Name}</name></assembly><members>
                  <member name="T:PowerForge.Tests.WebReferenceDocumentationFixture"><summary>Fixture.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceDocumentationFixture.Dispose"><inheritdoc/></member>
                  <member name="M:PowerForge.Tests.WebReferenceDocumentationFixture.ToString"><inheritdoc/></member>
                </members></doc>
                """);
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "apidocs", type = "CSharp", assemblies = new[] { assembly },
                    xmls = new[] { xml }, @out = Path.Combine(root, "api"), format = "json", includeUndocumented = false, failOnWarnings = true } }
            }));

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.True(result.Success, string.Join("; ", result.Steps.Select(step => step.Message)));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "api", "types", "powerforge-tests-webreferencedocumentationfixture.json")));
            var methods = json.RootElement.GetProperty("methods").EnumerateArray().ToArray();
            Assert.Contains("resources", methods.Single(method => method.GetProperty("name").GetString() == "Dispose").GetProperty("summary").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("string", methods.Single(method => method.GetProperty("name").GetString() == "ToString").GetProperty("summary").GetString()!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Generate_ResolvesEqualityUsingReferenceXmlOrBuiltInFallback(bool valueType, bool referenceXml)
    {
        WithRoot(root =>
        {
            // No TargetFrameworkAttribute: discovery has no targeting pack to consult.
            var assembly = Path.Combine(root, "EqualityFixture.dll");
            var builder = new PersistedAssemblyBuilder(new AssemblyName("EqualityFixture"), typeof(object).Assembly);
            var module = builder.DefineDynamicModule("EqualityFixture");
            var type = module.DefineType("EqualityFixture", TypeAttributes.Public | TypeAttributes.Sealed,
                valueType ? typeof(ValueType) : typeof(object));
            type.AddInterfaceImplementation(typeof(IEquatable<int>));
            DefineMethod(type, "Equals", typeof(bool), new[] { typeof(object) }, OpCodes.Ldc_I4_0);
            DefineMethod(type, "Equals", typeof(bool), new[] { typeof(int) }, OpCodes.Ldc_I4_0);
            DefineMethod(type, "GetHashCode", typeof(int), Type.EmptyTypes, OpCodes.Ldc_I4_0);
            DefineMethod(type, "ToString", typeof(string), Type.EmptyTypes, OpCodes.Ldnull);
            type.CreateType();
            builder.Save(assembly);
            if (referenceXml)
            {
                File.Copy(typeof(object).Assembly.Location, Path.Combine(root, "System.Private.CoreLib.dll"));
                var ids = new[] { "M:System.Object.Equals(System.Object)", "M:System.ValueType.Equals(System.Object)",
                    "M:System.IEquatable`1.Equals(`0)", "M:System.Object.GetHashCode", "M:System.ValueType.GetHashCode",
                    "M:System.Object.ToString", "M:System.ValueType.ToString" };
                File.WriteAllText(Path.Combine(root, "System.Private.CoreLib.xml"), "<doc><members>" +
                    string.Concat(ids.Select(id => $"<member name=\"{id}\"><summary>Reference XML contract.</summary><returns>Reference result.</returns></member>")) +
                    "</members></doc>");
            }
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:EqualityFixture"><summary>Fixture.</summary></member>
                  <member name="M:EqualityFixture.Equals(System.Object)"><inheritdoc/></member>
                  <member name="M:EqualityFixture.Equals(System.Int32)"><inheritdoc/></member>
                  <member name="M:EqualityFixture.GetHashCode"><inheritdoc/></member>
                  <member name="M:EqualityFixture.ToString"><inheritdoc/></member>
                </members></doc>
                """);

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = assembly, OutputPath = Path.Combine(root, "api"),
                Format = "json", IncludeUndocumentedTypes = false
            });

            Assert.Empty(result.Warnings);
            Assert.Equal(1, result.TypeCount);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "api", "types", "equalityfixture.json")));
            var methods = json.RootElement.GetProperty("methods").EnumerateArray()
                .Where(method => method.GetProperty("name").GetString() is "Equals" or "GetHashCode" or "ToString").ToArray();
            Assert.Equal(4, methods.Length);
            foreach (var method in methods)
            {
                var summary = method.GetProperty("summary").GetString();
                if (referenceXml) Assert.Equal("Reference XML contract.", summary);
                else Assert.Contains(method.GetProperty("name").GetString() switch
                {
                    "Equals" => "equal", "GetHashCode" => "hash code", _ => "string"
                }, summary!, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("returns").GetString()));
            }
        });
    }

    [Fact]
    public void Generate_PrefersReferenceXmlFromLaterAssemblyOverEarlierFallback()
    {
        WithRoot(root =>
        {
            var assemblies = new List<string>();
            var xmls = new List<string>();
            foreach (var name in new[] { "WithoutXml", "WithXml" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var assembly = Path.Combine(directory, name + ".dll");
                var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
                var type = builder.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public, typeof(object));
                DefineMethod(type, "ToString", typeof(string), Type.EmptyTypes, OpCodes.Ldnull);
                type.CreateType();
                builder.Save(assembly);
                var xml = Path.Combine(directory, "docs.xml");
                File.WriteAllText(xml, $"<doc><assembly><name>{name}</name></assembly><members>" +
                    $"<member name=\"T:{name}\"><summary>Fixture.</summary></member>" +
                    $"<member name=\"M:{name}.ToString\"><inheritdoc/></member></members></doc>");
                assemblies.Add(assembly);
                xmls.Add(xml);
            }
            var referenceRoot = Path.GetDirectoryName(assemblies[1])!;
            File.Copy(typeof(object).Assembly.Location, Path.Combine(referenceRoot, "System.Private.CoreLib.dll"));
            File.WriteAllText(Path.Combine(referenceRoot, "System.Private.CoreLib.xml"),
                "<doc><members><member name=\"M:System.Object.ToString\"><summary>Reference XML contract.</summary></member></members></doc>");

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPaths = xmls, AssemblyPaths = assemblies, OutputPath = Path.Combine(root, "api"),
                Format = "json", IncludeUndocumentedTypes = false
            });

            Assert.Empty(result.Warnings);
            Assert.Equal(2, result.TypeCount);
            foreach (var name in new[] { "withoutxml", "withxml" })
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "api", "types", name + ".json")));
                var method = json.RootElement.GetProperty("methods").EnumerateArray().Single(method => method.GetProperty("name").GetString() == "ToString");
                Assert.Equal("Reference XML contract.", method.GetProperty("summary").GetString());
            }
        });
    }

    [Fact]
    public void PipelineCache_TracksPluralAssemblyBytes()
    {
        WithRoot(root =>
        {
            var assembly = Path.Combine(root, "CacheFixture.dll");
            var xml = Path.Combine(root, "docs.xml");
            void WriteAssembly(string methodName)
            {
                var builder = new PersistedAssemblyBuilder(new AssemblyName("CacheFixture"), typeof(object).Assembly);
                var type = builder.DefineDynamicModule("CacheFixture").DefineType("CacheFixture", TypeAttributes.Public);
                DefineMethod(type, methodName, typeof(int), Type.EmptyTypes, OpCodes.Ldc_I4_0);
                type.CreateType();
                builder.Save(assembly);
            }
            WriteAssembly("BeforeRebuild");
            File.WriteAllText(xml, "<doc><assembly><name>CacheFixture</name></assembly><members><member name=\"T:CacheFixture\"><summary>Fixture.</summary></member></members></doc>");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new { cache = true, steps = new[] { new
            {
                task = "apidocs", assemblies = new[] { assembly }, xml, @out = Path.Combine(root, "api"), format = "json", includeUndocumented = false
            } } }));
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.True(Assert.Single(WebPipelineRunner.RunPipeline(pipeline, logger: null).Steps).Cached);
            var timestamp = File.GetLastWriteTimeUtc(assembly);
            WriteAssembly("AfterRebuild_");
            File.SetLastWriteTimeUtc(assembly, timestamp);

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.True(result.Success);
            Assert.False(Assert.Single(result.Steps).Cached);
            var output = File.ReadAllText(Path.Combine(root, "api", "types", "cachefixture.json"));
            Assert.Contains("AfterRebuild_", output);
            Assert.DoesNotContain("BeforeRebuild", output);
        });
    }

    [Theory]
    [InlineData("assemblies")]
    [InlineData("assemblyPaths")]
    public void PipelineCache_RefreshesPluralFrameworkXmlWhenAddedOrChanged(string assemblyInput)
    {
        WithRoot(root =>
        {
            var assembly = Path.Combine(root, "CacheFixture.dll");
            var builder = new PersistedAssemblyBuilder(new AssemblyName("CacheFixture"), typeof(object).Assembly);
            var type = builder.DefineDynamicModule("CacheFixture").DefineType("CacheFixture", TypeAttributes.Public);
            DefineMethod(type, "ToString", typeof(string), Type.EmptyTypes, OpCodes.Ldnull);
            type.CreateType();
            builder.Save(assembly);
            File.Copy(typeof(object).Assembly.Location, Path.Combine(root, "System.Private.CoreLib.dll"));
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><assembly><name>CacheFixture</name></assembly><members><member name=\"T:CacheFixture\"><summary>Fixture.</summary></member>" +
                "<member name=\"M:CacheFixture.ToString\"><inheritdoc/></member></members></doc>");
            var step = new Dictionary<string, object>
            {
                ["task"] = "apidocs", [assemblyInput] = new[] { assembly }, ["xml"] = xml,
                ["out"] = Path.Combine(root, "api"), ["format"] = "json", ["includeUndocumented"] = false, ["failOnWarnings"] = true
            };
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new { cache = true, steps = new[] { step } }));
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.True(Assert.Single(WebPipelineRunner.RunPipeline(pipeline, logger: null).Steps).Cached);
            var output = Path.Combine(root, "api", "types", "cachefixture.json");
            Assert.Contains("represents the current object", File.ReadAllText(output));
            var reference = Path.Combine(root, "System.Private.CoreLib.xml");
            string Contract(string summary) => "<doc><members><member name=\"M:System.Object.ToString\"><summary>" + summary + "</summary></member></members></doc>";
            File.WriteAllText(reference, Contract("Initial reference summary."));

            var added = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(added.Success);
            Assert.False(Assert.Single(added.Steps).Cached);
            Assert.Contains("Initial reference summary.", File.ReadAllText(output));
            Assert.True(Assert.Single(WebPipelineRunner.RunPipeline(pipeline, logger: null).Steps).Cached);
            var timestamp = File.GetLastWriteTimeUtc(reference);
            File.WriteAllText(reference, Contract("Updated reference summary."));
            File.SetLastWriteTimeUtc(reference, timestamp);

            var changed = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(changed.Success);
            Assert.False(Assert.Single(changed.Steps).Cached);
            Assert.Contains("Updated reference summary.", File.ReadAllText(output));
            Assert.DoesNotContain("Initial reference summary.", File.ReadAllText(output));
        });
    }

    [Fact]
    public void Pipeline_StillFailsForMissingLibraryBaseDocumentation()
    {
        WithRoot(root =>
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:PowerForge.Tests.WebReferenceNamedDerived"><summary>Derived.</summary></member>
                  <member name="M:PowerForge.Tests.WebReferenceNamedDerived.Run"><inheritdoc/></member>
                </members></doc>
                """);
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "apidocs", type = "CSharp", assembly = typeof(WebReferenceNamedDerived).Assembly.Location,
                    xml, @out = Path.Combine(root, "api"), format = "json", includeUndocumented = false, failOnWarnings = true } }
            }));

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.False(result.Success);
            Assert.Contains("Implicit inheritdoc could not be resolved for M:PowerForge.Tests.WebReferenceNamedDerived.Run", Assert.Single(result.Steps).Message);
        });
    }

    private static void DefineMethod(TypeBuilder type, string name, Type result, Type[] parameters, OpCode value)
    {
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            result, parameters);
        var il = method.GetILGenerator();
        il.Emit(value);
        il.Emit(OpCodes.Ret);
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-framework-inheritdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
