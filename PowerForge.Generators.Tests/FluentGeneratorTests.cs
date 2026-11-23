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
    public void InlinedSettingsPresetsAndCopiesConfigureTheOwner() {
        const string library = """
            using System;
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                public enum BarMode { Grouped, Stacked }

                [Fluent]
                public class Chart {
                    [FluentInline]
                    public ChartOptions Options { get; } = new();
                    public string? Title { get; set; }
                }

                public sealed class LineChart : Chart {
                }

                public sealed class ChartOptions {
                    public string? Theme { get; set; }
                    public bool IsAnimated { get; set; }
                    [FluentPreset("Stacked", BarMode.Stacked)]
                    [FluentPreset("Grouped", BarMode.Grouped)]
                    public BarMode Mode { get; set; }
                    [FluentPreset("PlainLine", typeof(LineStyle), nameof(LineStyle.Plain))]
                    [FluentPreset("DashedLine", typeof(LineStyle), nameof(LineStyle.Dashed))]
                    public LineStyle? Line { get; set; }
                    [FluentPreset("Wide", 2.5)]
                    public double Width { get; set; }
                    [FluentPreset("Untitled", null)]
                    public string? Caption { get; set; }
                    public Legend? Legend { get; set; }
                    public Legend Tooltip { get; } = new();
                    public List<string> Notes { get; } = new();
                }

                public sealed class LineStyle {
                    public static readonly LineStyle Dashed = new() { Name = "dashed" };
                    public string? Name { get; set; }
                    public static LineStyle Plain() => new() { Name = "plain" };
                }

                [Fluent(Copy = "Clone")]
                public class LegendBase {
                    public string? Position { get; set; }
                    internal LegendBase Clone() => (LegendBase)MemberwiseClone();
                }

                // Inherits the copy method, which returns the base type, and hides it with a method the copy must not call.
                public sealed class Legend : LegendBase {
                    public new int Clone() => 0;
                }

                public static class Usage {
                    public static object[] Run() {
                        LineChart chart = new LineChart().Title("Sales").Theme("dark").Animated().Stacked().PlainLine().Wide().Note("n")
                            .Options(options => options.Caption("caption")).Untitled();
                        Legend? captured = null;
                        chart.Legend(legend => { captured = legend; legend.Position("Top"); });
                        captured!.Position("Left");
                        bool threw = false;
                        try {
                            chart.Legend(legend => { legend.Position("Bottom"); throw new InvalidOperationException(); });
                        } catch (InvalidOperationException) {
                            threw = true;
                        }
                        Legend? tooltip = null;
                        chart.Tooltip(t => { tooltip = t; t.Position("Right"); });
                        ChartOptions dashed = new ChartOptions().DashedLine().Grouped();
                        return new object[] { chart.Title!, chart.Options.Theme!, chart.Options.IsAnimated, chart.Options.Mode.ToString(), chart.Options.Line!.Name!,
                            chart.Options.Width, chart.Options.Caption ?? "null", chart.Options.Notes.Count, chart.Options.Legend!.Position!, threw,
                            ReferenceEquals(tooltip, chart.Options.Tooltip), dashed.Line!.Name!, dashed.Mode.ToString() };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library);
        Assert.Empty(generatorDiagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        // Enum presets read as the member, not as a cast of its value.
        Assert.Contains(compilation.SyntaxTrees, tree => tree.ToString().Contains("= global::Sample.BarMode.Stacked;", StringComparison.Ordinal));

        // The legend callback's copy was committed as a fresh copy, so neither the later edit through the captured reference
        // nor the failing callback reached it; the read-only tooltip was configured in place.
        Assert.Equal(new object[] { "Sales", "dark", true, "Stacked", "plain", 2.5, "null", 1, "Top", true, true, "dashed", "Grouped" }, Execute(compilation));
    }

    [Fact]
    public void PresetsAssignFittingIntegralConstantsAndTheStaticMemberThatWasAccepted() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Gauge {
                    [FluentPreset("Small", 1)]
                    public byte Size { get; set; }
                    [FluentPreset("Few", 5)]
                    public uint Count { get; set; }
                    [FluentPreset("Near", 2)]
                    public short? Offset { get; set; }
                    [FluentPreset("Far", 7L)]
                    public ulong Distance { get; set; }
                    [FluentPreset("Standard", typeof(Styles), "Default")]
                    public int Style { get; set; }
                }

                public class BaseStyles {
                    public static readonly int Default = 3;
                }

                // Hides the base field with one an int property cannot take.
                public sealed class Styles : BaseStyles {
                    public static new readonly string Default = "plain";
                }

                public static class Usage {
                    public static object[] Run() {
                        Gauge gauge = new Gauge().Small().Few().Near().Far().Standard();
                        return new object[] { gauge.Size, gauge.Count, gauge.Offset!.Value, gauge.Distance, gauge.Style };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library);
        Assert.Empty(generatorDiagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));

        Assert.Equal(new object[] { (byte)1, 5U, (short)2, 7UL, 3 }, Execute(compilation));
    }

    [Fact]
    public void InlinedSettingsBringTheirForwardsToTheOwner() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Chart {
                    [FluentInline]
                    public ChartOptions Options { get; } = new();
                }

                public sealed class LineChart : Chart {
                }

                [FluentForward("Content", typeof(ContentBuilder), "Replace")]
                public sealed class ChartOptions {
                    public string? Content { get; set; }
                    public string? Theme { get; set; }
                }

                public static class ContentBuilder {
                    public static void Replace(ChartOptions options, string text) => options.Content = "text:" + text;
                    public static void Replace(ChartOptions options, int count) => options.Content = "count:" + count;
                }

                public static class Usage {
                    public static object[] Run() {
                        LineChart chart = new LineChart().Content("a").Theme("dark");
                        LineChart counted = new LineChart().Content(3);
                        LineChart configured = new LineChart().Options(o => o.Content("b"));
                        return new object[] { chart.Options.Content!, chart.Options.Theme!, counted.Options.Content!, configured.Options.Content! };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library);
        Assert.Empty(generatorDiagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));

        // Content(...) goes through the forward on the owner too, never straight to the property.
        Assert.Equal(new object[] { "text:a", "dark", "count:3", "text:b" }, Execute(compilation));
    }

    [Fact]
    public void ShortcutsThatCannotWorkAreBuildErrors() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Chart {
                    [FluentInline]
                    public Options Options { get; } = new();
                    [FluentInline]
                    public string? Name { get; set; }
                    public string? Theme { get; set; }
                    public Chart Animated(bool value) => this;
                    [FluentPreset("Big", "large")]
                    public int Size { get; set; }
                    [FluentPreset("Missing", typeof(Chart), "Nowhere")]
                    public string? Label { get; set; }
                    [FluentPreset("Fixed", 1)]
                    public int Fixed { get; } = 0;
                    [FluentPreset("Visible", false)]
                    public bool IsVisible { get; set; }
                    [FluentPreset("Huge", 256)]
                    public byte Huge { get; set; }
                    [FluentPreset("Negative", -1)]
                    public uint Count { get; set; }
                }

                public sealed class Options {
                    public string? Theme { get; set; }
                    public bool IsAnimated { get; set; }
                    public Broken? Broken { get; set; }
                }

                [Fluent(Copy = "Clone")]
                public sealed class Broken {
                    public int Size { get; set; }
                    public Broken Clone(bool deep) => this;
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = Run(library);

        // Theme is both the owner's and the inlined one (PFF003); Animated(bool) exists on the owner (PFF002); the preset
        // Visible() takes the short form of Visible(bool value = true) (PFF003); a string preset for an int, a missing static
        // member, a get-only property, 256 for a byte and -1 for a uint (PFF010); Clone needs no parameters (PFF011); Name is
        // no settings type (PFF012).
        Assert.Equal(new[] { "PFF002", "PFF003", "PFF003", "PFF010", "PFF010", "PFF010", "PFF010", "PFF010", "PFF011", "PFF012" }, diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
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
                    [FluentInline]
                    public Settings Inlined { get; } = new Settings();
                }

                [Fluent(Copy = ""Copy"")]
                public sealed class Settings {
                    [FluentPreset(""Large"", 10L)]
                    public long Size { get; set; }
                    public Settings Copy() => (Settings)MemberwiseClone();
                }

                public static class Usage {
                    public static object[] Run() {
                        Legacy legacy = new Legacy().Title(""old"").Visible().Tag(""a"").Settings(s => s.Size(4)).Large();
                        return new object[] { legacy.Title, legacy.IsVisible, legacy.Tags.Count, legacy.Settings.Size, legacy.Inlined.Size };
                    }
                }
            }";

        (Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics) = Run(library, LanguageVersion.CSharp7_3);
        Assert.Empty(generatorDiagnostics);

        Assert.Equal(new object[] { "old", true, 1, 4L, 10L }, Execute(compilation));
    }

    [Fact]
    public void SettingsFromAReferencedLibraryAreInlinedAndConfigured() {
        const string baseLibrary = @"
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                // The copy method is internal, so a consuming library configures this type in place.
                [Fluent(Copy = ""Clone"")]
                public sealed class BlockOptions {
                    public bool ShowCard { get; set; } = true;
                    public string Theme { get; set; } = ""light"";
                    internal BlockOptions Clone() => (BlockOptions)MemberwiseClone();
                }

                [Fluent]
                public abstract class Block<TSelf> where TSelf : Block<TSelf> {
                    [FluentInline]
                    public BlockOptions Options { get; } = new BlockOptions();
                }
            }";
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string derivedLibrary = @"
            using Sample.Blocks;

            namespace Sample {
                public sealed class Card : Block<Card> {
                    public string? Title { get; set; }

                    public BlockOptions Footer { get; set; } = new BlockOptions();
                }

                public static class Usage {
                    public static object[] Run() {
                        Card card = new Card().Title(""Uptime"").ShowCard(false).Theme(""dark"").Footer(f => f.Theme(""muted""));
                        return new object[] { card.Title!, card.Options.ShowCard, card.Options.Theme, card.Footer.Theme };
                    }
                }
            }";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(derivedLibrary, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, LanguageVersion version = LanguageVersion.Latest, bool prefixed = false) =>
        TestCompiler.Run(source, version, prefixed ? new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" } : null, new FluentGenerator());

    private static object[] Execute(Compilation compilation) => (object[])TestCompiler.Load(compilation).GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!;
}
