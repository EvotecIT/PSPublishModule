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
                    [Fluent("Label")]
                    public string? Caption { get; set; }
                    public string Label { get; set; } = "";
                    [Fluent("Handler")]
                    public Action<object>? Handler { get; set; }
                    [Fluent("not valid")]
                    public string? Note { get; set; }
                }

                public static class ViewExtensions {
                    public static View Title(this View view, string? title) => view;
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = Run(library);

        // Formatter has no verb; Title exists; Modes and Mode, Label and Caption (string? vs string) share a signature;
        // Handler would invoke the delegate property; "not valid" is no identifier.
        Assert.Equal(new[] { "PFF001", "PFF002", "PFF002", "PFF003", "PFF003", "PFF005" }, diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void UnusualButValidModelsCompile() {
        const string library = """
            using System.Collections;
            using System.Collections.Generic;
            using PowerForge.Fluent;

            [Fluent]
            public sealed class GlobalModel {
                public string? Name { get; set; }
            }

            namespace Sample {
                [Fluent]
                public sealed class Box<T> where T : class, new() {
                    public T? Content { get; set; }
                }

                [FluentForward("Attach", typeof(Attachments), "Set")]
                public interface IAttachable {
                }

                public static class Attachments {
                    public static void Set(IAttachable owner, string valueInstance) => ((Value)owner).@event = valueInstance;
                }

                public class Pair<TFirst, TSecond> {
                }

                // None of these apply to the models below: a fixed argument differs, a constraint fails, or one type
                // parameter would need two different types.
                public static class PairExtensions {
                    public static Pair<string, T> Title<T>(this Pair<string, T> pair, string? title) => pair;
                    public static Pair<string, T> Note<T>(this Pair<string, T> pair, string? note) where T : struct => pair;
                    public static Pair<T, T> Same<T>(this Pair<T, T> pair, string? same) => pair;
                    public static Pair<T, U> Stroke<T, U>(this Pair<T, U> pair, string? stroke) where U : T => pair;
                    public static T Fill<T>(this T shape, string? fill) where T : struct => shape;
                }

                // A derivable model under an abstract marked base, and a sealed override, reuse the base's methods.
                [Fluent]
                public abstract class Shape {
                    public string? Fill { get; set; }
                }

                public class Rect : Shape {
                    public virtual int Width { get; set; }
                }

                public sealed class Square : Rect {
                    public override int Width { get; set; }
                }

                public sealed class Entry {
                    public string? Text { get; set; }
                }

                [Fluent]
                public sealed class Journal {
                    public List<Entry?> Entries { get; } = new();
                }

                [Fluent]
                public sealed class Pairing : Pair<int, bool> {
                    public string? Title { get; set; }
                    public string? Same { get; set; }
                    public string? Stroke { get; set; }
                }

                [Fluent]
                public sealed class Labelled : Pair<string, string> {
                    public string? Note { get; set; }
                }

                [Fluent]
                public sealed class Value : IAttachable {
                    public int Amount { get; set; }
                    public string? @event { get; set; }
                    public Tags Tags { get; } = new();
                    public List<string> Hidden { private get; set; } = new();
                }

                // Add is only reachable through ICollection<string>.
                public sealed class Tags : ICollection<string> {
                    private readonly List<string> _items = new();
                    public int Count => _items.Count;
                    public bool IsReadOnly => false;
                    void ICollection<string>.Add(string item) => _items.Add(item);
                    public void Clear() => _items.Clear();
                    public bool Contains(string item) => _items.Contains(item);
                    public void CopyTo(string[] array, int index) => _items.CopyTo(array, index);
                    public bool Remove(string item) => _items.Remove(item);
                    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                public class Base {
                    public string? Title { get; set; }
                }

                [Fluent]
                public sealed class Derived : Base {
                }

                public static class Usage {
                    public static object[] Run() {
                        Value value = new Value().Amount(3).@event("start").Tag("a").Tags(new[] { "b" });
                        Box<Value> box = new Box<Value>().Content(value);
                        GlobalModel global = new GlobalModel().Name("global");
                        Derived derived = new Derived().Title("inherited");
                        Pairing pairing = new Pairing().Title("pair").Same("same");
                        Labelled labelled = new Labelled().Note("note");
                        pairing.Stroke("stroke");
                        Rect rect = new Rect().Fill("red").Width(2);
                        Square square = new Square().Fill("blue").Width(3);
                        Journal journal = new Journal().Entry(entry => entry.Text("entry"));
                        Value attached = new Value().Attach("attached");
                        return new object[] { box.Content!.Amount, value.@event!, value.Tags.Count, global.Name!, derived.Title!, pairing.Title!, attached.@event!, pairing.Same!, labelled.Note!, pairing.Stroke!, rect.Width, square.Fill!, square.Width, journal.Entries[0]!.Text! };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library);
        Assert.Empty(generatorDiagnostics);

        Assert.Equal(new object[] { 3, "start", 2, "global", "inherited", "pair", "attached", "same", "note", "stroke", 2, "blue", 3, "entry" }, Execute(compilation));
    }

    [Fact]
    public void PrefixedNamingKeepsConsoleMonsterNamesAndLetsExistingMethodsWin() {
        const string library = """
            using System;
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Widget {
                    public string? Title { get; set; }
                    public bool IsVisible { get; set; }
                    public List<string> Tags { get; } = new();
                    public Func<int, string>? Formatter { get; set; }
                }

                public sealed class Panel : Widget {
                    public int Width { get; set; }
                }

                // Handwritten helpers share the namespace's fluent class; a matching one wins over the generated method.
                public static partial class SampleFluent {
                    public static TSelf Title<TSelf>(this TSelf widget, string? title) where TSelf : Widget {
                        widget.Title = "handwritten " + title;
                        return widget;
                    }
                }

                public static class Usage {
                    public static object[] Run() {
                        Panel panel = new Panel().Title("panel").Visible().Width(3)
                            .AddTag("a").AddTags("b", "c").AddTags(new List<string> { "d" })
                            .Configure(p => p.Formatter = n => "#" + n);
                        return new object[] { panel.Title!, panel.IsVisible, panel.Width, string.Join(",", panel.Tags), panel.Formatter!(1) };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library, prefixed: true);
        Assert.Empty(generatorDiagnostics);

        Assert.Equal(new object[] { "handwritten panel", true, 3, "a,b,c,d", "#1" }, Execute(compilation));
    }

    [Fact]
    public void GeneratedCodeCompilesAsCSharp73() {
        const string library = @"
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Legacy {
                    public string Title { get; set; }
                    public bool IsVisible { get; set; }
                    public List<string> Tags { get; set; } = new List<string>();
                    public Settings Settings { get; set; }
                }

                public sealed class Settings {
                    public int Size { get; set; }
                }

                public static class Usage {
                    public static object[] Run() {
                        Legacy legacy = new Legacy().Title(""old"").Visible().Tag(""a"").Settings(s => s.Size(4));
                        return new object[] { legacy.Title, legacy.IsVisible, legacy.Tags.Count, legacy.Settings.Size };
                    }
                }
            }";

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library, LanguageVersion.CSharp7_3);
        Assert.Empty(generatorDiagnostics);

        Assert.Equal(new object[] { "old", true, 1, 4 }, Execute(compilation));
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, LanguageVersion version = LanguageVersion.Latest, bool prefixed = false) =>
        TestCompiler.Run(source, version, prefixed ? new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" } : null, new FluentGenerator());

    private static object[] Execute(Compilation compilation) => (object[])TestCompiler.Load(compilation).GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!;
}
