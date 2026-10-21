using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiDocsFrameworkFallbackTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Generate_ResolvesFrameworkMembersFromAssemblyListsOrWithoutTargetingPack(bool assemblyList, bool targetingPack)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-framework-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assembly = Path.Combine(root, "FrameworkFixture.dll");
            WriteFixture(assembly, "FrameworkFixture", targetingPack);
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><assembly><name>FrameworkFixture</name></assembly><members>
                  <member name="T:FrameworkFixture.Value"><summary>A value.</summary></member>
                  <member name="M:FrameworkFixture.Value.Equals(System.Object)"><inheritdoc/></member>
                  <member name="M:FrameworkFixture.Value.Equals(FrameworkFixture.Value)"><inheritdoc/></member>
                  <member name="M:FrameworkFixture.Value.GetHashCode"><inheritdoc/></member>
                  <member name="M:FrameworkFixture.Value.ToString"><inheritdoc/></member>
                </members></doc>
                """);
            var paths = new List<string> { assembly };
            var xmls = new List<string>();
            if (targetingPack)
            {
                // A pack-less assembly goes first and contributes the same framework contract.
                // Real reference XML from the next assembly must replace its summary-only fallback.
                var packless = Path.Combine(root, "PacklessFixture.dll");
                WriteFixture(packless, "PacklessFixture", targetingPack: false);
                paths.Insert(0, packless);
                var packlessXml = Path.Combine(root, "packless.xml");
                File.WriteAllText(packlessXml, "<doc><assembly><name>PacklessFixture</name></assembly><members>" +
                    "<member name=\"T:PacklessFixture.Value\"><summary>A value.</summary></member>" +
                    "<member name=\"M:PacklessFixture.Value.Equals(System.Object)\"><inheritdoc/></member></members></doc>");
                xmls.Add(packlessXml);
            }
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, XmlPaths = xmls, AssemblyPath = assemblyList ? null : assembly,
                AssemblyPaths = assemblyList ? paths : Array.Empty<string>(),
                OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.Empty(result.Warnings);
            Assert.Equal(targetingPack ? 2 : 1, result.TypeCount);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "frameworkfixture-value.json")));
            var methods = json.RootElement.GetProperty("methods").EnumerateArray()
                .Where(method => method.GetProperty("name").GetString() is "Equals" or "GetHashCode" or "ToString").ToArray();
            Assert.Equal(4, methods.Length);
            Assert.All(methods, method => Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("summary").GetString())));
            if (targetingPack)
            {
                // Reference XML has parameter/return contracts; the built-in summary does not.
                var equals = methods.Single(method => method.GetProperty("name").GetString() == "Equals" &&
                    method.GetProperty("parameters")[0].GetProperty("type").GetString() == "object");
                Assert.False(string.IsNullOrWhiteSpace(equals.GetProperty("returns").GetString()));
            }
            else
                Assert.Contains(methods, method => method.GetProperty("summary").GetString() == "Returns a hash code for the current object.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void WriteFixture(string assembly, string name, bool targetingPack)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        if (targetingPack)
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(TargetFrameworkAttribute).GetConstructor(new[] { typeof(string) })!,
                new object[] { ".NETCoreApp,Version=v10.0" }));
        var module = builder.DefineDynamicModule("Main");
        var type = module.DefineType(name + ".Value", TypeAttributes.Public, typeof(ValueType));
        var equatable = typeof(IEquatable<>).MakeGenericType(type);
        type.AddInterfaceImplementation(equatable);
        void Method(string name, Type returns, Type[] args, OpCode value)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual, returns, args);
            var il = method.GetILGenerator();
            il.Emit(value);
            il.Emit(OpCodes.Ret);
        }
        Method("Equals", typeof(bool), new[] { typeof(object) }, OpCodes.Ldc_I4_0);
        Method("Equals", typeof(bool), new Type[] { type }, OpCodes.Ldc_I4_0);
        Method("GetHashCode", typeof(int), Type.EmptyTypes, OpCodes.Ldc_I4_0);
        Method("ToString", typeof(string), Type.EmptyTypes, OpCodes.Ldnull);
        type.CreateType();
        builder.Save(assembly);
    }

    [Fact]
    public void Pipeline_UnresolvedLibraryOverrideStillFailsOnWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-library-inherit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "docs.xml"), """
                <doc><assembly><name>PowerForge.Tests</name></assembly><members>
                  <member name="T:PowerForge.Tests.WebMissingLibraryDocsDerived"><summary>Derived.</summary></member>
                  <member name="M:PowerForge.Tests.WebMissingLibraryDocsDerived.ToString"><inheritdoc/></member>
                </members></doc>
                """);
            File.WriteAllText(Path.Combine(root, "pipeline.json"), JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "apidocs", xml = "docs.xml", assemblies = new[] { typeof(WebMissingLibraryDocsDerived).Assembly.Location },
                    @out = "api", format = "json", includeUndocumented = false, failOnWarnings = true } }
            }));
            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);
            Assert.False(result.Success);
            var generated = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = Path.Combine(root, "docs.xml"), AssemblyPaths = new[] { typeof(WebMissingLibraryDocsDerived).Assembly.Location },
                OutputPath = Path.Combine(root, "api"), Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.Contains(generated.Warnings, warning => warning.Contains("Implicit inheritdoc could not be resolved", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class WebMissingLibraryDocsBase
{
    public override string ToString() => "Base";
}

public sealed class WebMissingLibraryDocsDerived : WebMissingLibraryDocsBase
{
    public override string ToString() => "Derived";
}
