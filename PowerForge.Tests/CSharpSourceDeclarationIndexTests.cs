using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using PowerForge.Web;

public sealed class CSharpSourceDeclarationIndexTests
{
    [Fact]
    public void FindsNestedGenericEnumAndEachFieldByDeclarationIdentity()
    {
        using var fixture = new Fixture();
        var type = fixture.Assembly.GetType("Example.Nested.Owner`1+State")!;
        var index = fixture.Index();
        Assert.Equal(new CSharpSourceDeclarationIndex.Location(fixture.Primary, 5), index.FindType(type));
        Assert.Equal(new CSharpSourceDeclarationIndex.Location(fixture.Primary, 7), index.FindField(type.GetField("Ready")!));
        var owner = fixture.Assembly.GetType("Example.Nested.Owner`1")!.MakeGenericType(typeof(int));
        Assert.Equal(9, index.FindField(owner.GetField("First")!)!.Line);
        Assert.Equal(9, index.FindField(owner.GetField("Second")!)!.Line);
        Assert.Null(index.FindType(typeof(string)));
    }

    [Fact]
    public void UsesCompiledConditionalBranchAndCanonicalPartialFile()
    {
        using var fixture = new Fixture();
        var owner = fixture.Assembly.GetType("Example.Nested.Owner`1")!;
        var index = fixture.Index();
        Assert.Equal(fixture.Primary, index.FindType(owner)!.Path);
        Assert.Equal(11, index.FindField(owner.GetField("Conditional")!)!.Line);
    }

    [Fact]
    public void RejectsChangedSourceInsteadOfLinkingUncompiledDeclarations()
    {
        using var fixture = new Fixture();
        File.AppendAllText(fixture.Primary, "\n// Changed after compilation.\n");
        var type = fixture.Assembly.GetType("Example.Nested.Owner`1+State")!;
        var index = fixture.Index();
        Assert.Null(index.FindType(type));
        Assert.Null(index.FindField(type.GetField("Ready")!));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-csharp-source-" + Guid.NewGuid().ToString("N"));
        private readonly AssemblyLoadContext _context = new("source-declarations-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        private readonly MemoryStream _pdb = new();
        internal string Primary { get; }
        internal System.Reflection.Assembly Assembly { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            Primary = Path.Combine(_root, "Owner.cs");
            File.WriteAllText(Primary, """
                namespace Example.Nested;
                public partial class Owner<T>
                {
                    // Enums have no method sequence points.
                    public enum State
                    {
                        Ready,
                    }
                    public static int First, Second;
                #if COMPILED_BRANCH
                    public const int Conditional = 1;
                #else
                    public const int Conditional = 2;
                #endif
                    public int RunPrimary() => 1;
                }
                """, new UTF8Encoding(false));
            var other = Path.Combine(_root, "Owner.Alpha.cs");
            File.WriteAllText(other, "namespace Example { namespace Nested { public partial class Owner<T> { public int Run() => 1; } } }", new UTF8Encoding(false));
            var options = CSharpParseOptions.Default.WithPreprocessorSymbols("COMPILED_BRANCH");
            var trees = new[] { other, Primary }.Select(path =>
            {
                using var source = File.OpenRead(path);
                return CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), options, path);
            });
            var compilation = CSharpCompilation.Create("DeclarationFixture", trees,
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
            using var pe = new MemoryStream();
            var result = compilation.Emit(pe, _pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            pe.Position = 0;
            _pdb.Position = 0;
            Assembly = _context.LoadFromStream(pe, _pdb);
        }

        internal CSharpSourceDeclarationIndex Index()
        {
            _pdb.Position = 0;
            using var provider = MetadataReaderProvider.FromPortablePdbStream(_pdb, MetadataStreamOptions.LeaveOpen);
            return CSharpSourceDeclarationIndex.Create(provider.GetMetadataReader(), _root);
        }

        public void Dispose()
        {
            _context.Unload();
            _pdb.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
