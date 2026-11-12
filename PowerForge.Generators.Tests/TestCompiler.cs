using System.Collections.Immutable;
using System.Management.Automation;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>Compiles a library with the generators the way a consuming project's build does, including its MSBuild properties.</summary>
internal static class TestCompiler {
    internal static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, LanguageVersion version, IDictionary<string, string>? properties, params IIncrementalGenerator[] generators) {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Append(typeof(PSCmdlet).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path));
        CSharpParseOptions parse = new CSharpParseOptions(version);
        NullableContextOptions nullable = version >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable;
        CSharpCompilation compilation = CSharpCompilation.Create("Sample" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source, parse) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators.Select(generator => generator.AsSourceGenerator()), parseOptions: parse,
            optionsProvider: new BuildProperties(properties ?? new Dictionary<string, string>()));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics);
    }

    internal static Assembly Load(Compilation compilation) {
        using MemoryStream image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(image.ToArray());
    }

    // The MSBuild properties a consuming project passes to the generators.
    private sealed class BuildProperties : AnalyzerConfigOptionsProvider {
        private readonly Options _global;

        public BuildProperties(IDictionary<string, string> properties) {
            _global = new Options(properties.ToDictionary(pair => "build_property." + pair.Key, pair => pair.Value));
        }

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options : AnalyzerConfigOptions {
            internal static readonly Options Empty = new Options(new Dictionary<string, string>());
            private readonly Dictionary<string, string> _values;

            internal Options(Dictionary<string, string> values) => _values = values;

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => _values.TryGetValue(key, out value);
        }
    }
}
