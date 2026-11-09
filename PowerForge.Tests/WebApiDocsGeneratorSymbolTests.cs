using System.Text;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorSymbolTests
{
    [Theory]
    [InlineData(DebugInformationFormat.PortablePdb, false, false)]
    [InlineData(DebugInformationFormat.PortablePdb, true, false)]
    [InlineData(DebugInformationFormat.Embedded, false, false)]
    [InlineData(DebugInformationFormat.Embedded, false, true)]
    [InlineData(DebugInformationFormat.Embedded, true, false)]
    public void SourceLinksUseMatchingPortableOrEmbeddedSymbols(DebugInformationFormat format, bool staleSidecar, bool damagedEmbedded)
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
                    options: new EmitOptions(debugInformationFormat: format, pdbFilePath: pdb));
                Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            }
            if (staleSidecar)
            {
                var otherSource = source.Replace("42", "43", StringComparison.Ordinal);
                var otherPath = Path.Combine(root, "Other.cs");
                File.WriteAllText(otherPath, otherSource, Encoding.UTF8);
                var unrelated = compilation.WithAssemblyName("Unrelated" + Guid.NewGuid().ToString("N"))
                    .RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                        SourceText.From(otherSource, Encoding.UTF8), path: otherPath));
                using var otherAssembly = new MemoryStream();
                using var symbols = File.Create(pdb);
                var emitted = unrelated.Emit(otherAssembly, pdbStream: symbols,
                    options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: pdb));
                Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            }
            else if (format == DebugInformationFormat.Embedded)
                File.WriteAllText(pdb, "unreadable unrelated PDB");
            if (damagedEmbedded)
            {
                byte[] bytes = File.ReadAllBytes(dll);
                DebugDirectoryEntry entry;
                using (var pe = new PEReader(new MemoryStream(bytes)))
                    entry = pe.ReadDebugDirectory().Single(item => item.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
                using (var compressed = new MemoryStream(bytes, entry.DataPointer + 8, entry.DataSize - 8))
                using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
                using (var symbols = File.Create(pdb))
                    inflater.CopyTo(symbols);
                bytes[entry.DataPointer] = 0; // Break MPDB metadata while preserving executable and CodeView identity.
                File.WriteAllBytes(dll, bytes);
            }
            var xml = Path.ChangeExtension(dll, ".xml");
            File.WriteAllText(xml, $"<doc><assembly><name>{compilation.AssemblyName}</name></assembly><members /></doc>");
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPath = dll, XmlPath = xml, OutputPath = output, Format = "json",
                SourceRootPath = root, SourceUrlPattern = "https://example.invalid/{path}#L{line}"
            });
            bool expectedLinks = format == DebugInformationFormat.Embedded || !staleSidecar;
            Assert.Equal(!expectedLinks, result.Warnings.Any(warning => warning.Contains("Source links disabled", StringComparison.Ordinal)));
            var json = File.ReadAllText(Path.Combine(output, "types", "fixture-api.json"));
            Assert.Equal(expectedLinks, json.Contains("https://example.invalid/Fixture.cs#L3", StringComparison.Ordinal));
            // Generation must release both symbol formats, including on Windows.
            File.Move(dll, dll + ".retired");
            File.Move(pdb, pdb + ".retired");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
