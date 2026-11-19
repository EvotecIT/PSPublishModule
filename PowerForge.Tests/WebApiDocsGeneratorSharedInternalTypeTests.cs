using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorSharedInternalTypeTests
{
    // Source generators embed their attributes as internal types in every assembly that uses them, and the compiler
    // documents internal types, so each XML file lists the same internal type.
    [Theory]
    [InlineData("internal", true)]
    [InlineData("public", false)]
    public void Generate_OnlyComparesExportedTypesAcrossAssemblies(string sharedAccess, bool succeeds)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-apidocs-shared-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var shared = $"namespace Generated {{ {sharedAccess} sealed class MarkerAttribute : System.Attribute {{ }} }}";
            var first = Compile(root, "FirstApi", shared + " namespace First { public class Alpha { } }");
            var second = Compile(root, "SecondApi", shared + " namespace Second { public class Beta { } }");
            var firstXml = WriteXml(root, "FirstApi", "First.Alpha");
            var secondXml = WriteXml(root, "SecondApi", "Second.Beta");
            var output = Path.Combine(root, "api");

            var error = Record.Exception(() => WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPaths = new[] { first, second },
                XmlPaths = new[] { firstXml, secondXml },
                OutputPath = output,
                Format = "json"
            }));

            if (!succeeds)
            {
                Assert.Contains("'Generated.MarkerAttribute' is declared by multiple assemblies", Assert.IsType<InvalidDataException>(error).Message, StringComparison.Ordinal);
                return;
            }

            Assert.Null(error);
            var names = Directory.GetFiles(Path.Combine(output, "types"), "*.json")
                .Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("fullName").GetString())
                .OrderBy(static name => name, StringComparer.Ordinal);
            Assert.Equal(new[] { "First.Alpha", "Second.Beta" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteXml(string directory, string assembly, string publicType)
    {
        var path = Path.Combine(directory, assembly + ".xml");
        File.WriteAllText(path, $"""
            <doc><assembly><name>{assembly}</name></assembly><members>
              <member name="T:Generated.MarkerAttribute"><summary>Marks a type for the generator.</summary></member>
              <member name="T:{publicType}"><summary>A documented type.</summary></member>
            </members></doc>
            """);
        return path;
    }

    private static string Compile(string directory, string name, string source)
    {
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, name + ".dll");
        var emitted = compilation.Emit(path);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return path;
    }
}
