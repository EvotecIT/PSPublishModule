using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Generators.Fluent;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>
/// Compiles a library with the generator, as a consumer's build does, then runs code against the result.
/// </summary>
public sealed class FluentGeneratorTests {
    [Fact]
    public void ConventionNamesSetPropertiesAddChildrenAndKeepTheDerivedReceiver() {
        const string library = """
            using System;
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Chart {
                    public string? Title { get; set; }
                    public bool IsVisible { get; set; }
                    public Legend? Legend { get; set; }
                    public List<Series> Series { get; } = new();
                    [Fluent("FormatLabels")]
                    public Func<double, string>? LabelFormatter { get; set; }
                }

                public sealed class LineChart : Chart {
                    public double Tension { get; set; }
                }

                public sealed class Legend {
                    public string? Position { get; set; }
                }

                public sealed class Series {
                    public string? Name { get; set; }
                }

                public static class Usage {
                    public static object[] Run() {
                        LineChart chart = new LineChart()
                            .Title("Orders").Visible().Tension(0.4)
                            .Legend(legend => legend.Position("Bottom"))
                            .Series(new Series().Name("North"))
                            .Series(series => series.Name("South"))
                            .Series(new[] { new Series().Name("East") })
                            .FormatLabels(value => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                        return new object[] { chart.Title!, chart.IsVisible, chart.Tension, chart.Legend!.Position!,
                            string.Join(",", chart.Series.ConvertAll(s => s.Name)), chart.LabelFormatter!(2) };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library);
        Assert.Empty(generatorDiagnostics);
        object[] result = Execute(compilation);

        Assert.Equal(new object[] { "Orders", true, 0.4, "Bottom", "North,South,East", "2.0" }, result);
    }

    [Fact]
    public void AmbiguousOrUncallableMethodsAreBuildErrors() {
        const string library = """
            using System;
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class View {
                    public Func<string>? Formatter { get; set; }
                    public string? Title { get; set; }
                    public int Mode { get; set; }
                    public List<int> Modes { get; } = new();
                }

                public static class ViewExtensions {
                    public static View Title(this View view, string? title) => view;
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = Run(library);

        Assert.Equal(new[] { "PFF001", "PFF002", "PFF003" }, diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source) {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("Sample", new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FluentGenerator()).WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics);
    }

    private static object[] Execute(Compilation compilation) {
        using MemoryStream image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(image.ToArray());
        return (object[])assembly.GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!;
    }
}
