using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorSymbolTests
{
    [Theory]
    [InlineData(DebugInformationFormat.PortablePdb)]
    [InlineData(DebugInformationFormat.Embedded)]
    public void SourceLinksUseMatchingPortableOrEmbeddedSymbols(DebugInformationFormat format)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-symbol-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "Fixture.cs");
            var source = "namespace Fixture;\npublic class Api {\n public int Read() => 42;\n}\n";
            File.WriteAllText(sourcePath, source, Encoding.UTF8);
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: sourcePath);
            var compilation = CSharpCompilation.Create("SourceSymbols" + Guid.NewGuid().ToString("N"), new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var dll = Path.Combine(root, "Fixture.dll");
            var pdb = Path.ChangeExtension(dll, ".pdb");
            using (var assembly = File.Create(dll))
            using (var symbols = format == DebugInformationFormat.PortablePdb ? File.Create(pdb) : null)
            {
                var emitted = compilation.Emit(assembly, pdbStream: symbols,
                    options: new EmitOptions(debugInformationFormat: format));
                Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            }
            // An old sidecar must not override the symbols belonging to the embedded assembly.
            if (format == DebugInformationFormat.Embedded) File.WriteAllText(pdb, "stale unrelated PDB");
            var xml = Path.ChangeExtension(dll, ".xml");
            File.WriteAllText(xml, $"<doc><assembly><name>{compilation.AssemblyName}</name></assembly><members /></doc>");
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPath = dll, XmlPath = xml, OutputPath = output, Format = "json",
                SourceRootPath = root, SourceUrlPattern = "https://example.invalid/{path}#L{line}"
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Source links disabled", StringComparison.Ordinal));
            var json = File.ReadAllText(Path.Combine(output, "types", "fixture-api.json"));
            Assert.Contains("https://example.invalid/Fixture.cs#L3", json, StringComparison.Ordinal);
            // Generation must release both symbol formats, including on Windows.
            File.Move(dll, dll + ".retired");
            File.Move(pdb, pdb + ".retired");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
