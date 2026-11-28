using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Generators.Fluent;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>
/// Write paths that are not public setters ([FluentMutator], struct handles), bool? switches, nested and abstract types, and
/// properties whose getter hands out a new object: compiled with the generator, then run.
/// </summary>
public sealed class FluentGeneratorMutatorTests {
    [Theory]
    [InlineData(LanguageVersion.Latest)]
    [InlineData(LanguageVersion.CSharp7_3)]
    public void MutatorsWriteThroughMethodsWithTheirParametersAndDefaults(LanguageVersion version) {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                public enum Align { Left, Center, Right }

                [Fluent]
                public class Comment {
                    public string Log = "";
                    public bool Resolved { get; private set; }
                    public string Title { get; set; } = "";
                    public string Zones { get; private set; } = "";
                    public Margins Margins { get; } = new Margins();

                    [FluentMutator]
                    internal void SetResolved(bool resolved) { Resolved = resolved; }

                    /// <summary>Sets the title through the canonical path.</summary>
                    /// <param name="title">The new title.</param>
                    [FluentMutator]
                    internal Comment SetTitle(string title) { Log += "title;"; Title = title; return this; }

                    [FluentMutator("Zones")]
                    internal void ApplyZones(string left, string center = "c", Align align = Align.Right, decimal? scale = null, int count = default) {
                        Zones = left + "|" + center + "|" + align + "|" + (scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none") + "|" + count;
                    }
                }

                public sealed class Reply : Comment {
                    public string Author { get; set; } = "";
                }

                [Fluent]
                public sealed class Page {
                    [FluentInline]
                    public Margins Margins { get; } = new Margins();
                }

                public sealed class Margins {
                    public double Top { get; set; }
                    public string Value { get; private set; } = "";

                    [FluentMutator("Set")]
                    internal void SetAll(double top, double left) { Value = top + "," + left; }
                }

                public static class Usage {
                    public static object[] Run() {
                        Reply reply = new Reply().Author("Ann").Resolved().Title("Draft").Zones("l").Margins(m => m.Set(1, 2));
                        Reply other = new Reply().Resolved().Resolved(false).Zones("a", "b", Align.Left, 2.5m, 3);
                        Page page = new Page().Set(3, 4).Top(5);
                        return new object[] { reply.Author, reply.Resolved, reply.Title, reply.Log, reply.Zones, reply.Margins.Value,
                            other.Resolved, other.Zones, page.Margins.Value, page.Margins.Top };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library, version);
        Assert.Empty(diagnostics);

        // The mutator replaces the Title setter method, so the title goes through it ("title;" is logged).
        Assert.Equal(new object[] { "Ann", true, "Draft", "title;", "l|c|Right|none|0", "1,2", false, "a|b|Left|2.5|3", "3,4", 5.0 }, Execute(compilation));
    }

    [Fact]
    public void StructHandlesGetByValueMutatorsThatReturnTheSameHandle() {
        const string library = """
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                public sealed class Sheet {
                    public Dictionary<int, string> Styles { get; } = new Dictionary<int, string>();
                }

                [Fluent]
                [FluentForward("Fill", typeof(CellStyles), "Fill")]
                public readonly struct Cell {
                    private readonly Sheet _sheet;
                    public Cell(Sheet sheet, int index) { _sheet = sheet; Index = index; }
                    public int Index { get; }
                    public string Style => _sheet.Styles.TryGetValue(Index, out string? style) ? style : "";

                    [FluentMutator]
                    internal void SetBold(bool bold) { _sheet.Styles[Index] = Style + (bold ? "bold;" : "plain;"); }

                    [FluentMutator]
                    internal Cell SetBorder(string color, int width = 1) { _sheet.Styles[Index] = Style + "border:" + color + "/" + width + ";"; return this; }

                    internal void Append(string style) { _sheet.Styles[Index] = Style + style; }
                }

                internal static class CellStyles {
                    internal static void Fill(Cell cell, string color) => cell.Append("fill:" + color + ";");
                }

                public static class Usage {
                    public static object[] Run() {
                        Sheet sheet = new Sheet();
                        Cell cell = new Cell(sheet, 7);
                        Cell same = cell.Bold().Border("red").Bold(false).Border("blue", 2).Fill("green");
                        return new object[] { same.Index, sheet.Styles[7] };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);
        Assembly assembly = TestCompiler.Load(compilation);

        Assert.Equal(new object[] { 7, "bold;border:red/1;plain;border:blue/2;fill:green;" }, (object[])assembly.GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!);
        // By value in, the same struct out: no wrapper type, no ref receiver, and no setter methods for the handle's properties.
        MethodInfo[] methods = assembly.GetType("Sample.CellFluent")!.GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(new[] { "Bold", "Border", "Fill" }, methods.Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(methods, m => Assert.True(m.ReturnType == assembly.GetType("Sample.Cell") && m.GetParameters()[0].ParameterType == m.ReturnType));
    }

    [Fact]
    public void NullableBoolPropertiesFollowTheBoolRule() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Viewer {
                    public bool? IsCenterWindow { get; set; }
                    public bool? FitWindow { get; set; } = false;
                }

                public static class Usage {
                    public static object[] Run() {
                        Viewer viewer = new Viewer().CenterWindow().FitWindow(null);
                        Viewer cleared = new Viewer().CenterWindow(false);
                        return new object[] { viewer.IsCenterWindow!, viewer.FitWindow.HasValue, cleared.IsCenterWindow! };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);

        Assert.Equal(new object[] { true, false, false }, Execute(compilation));
    }

    [Fact]
    public void MarkedNestedTypesAndAbstractFluentPropertiesAreConfigured() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                public static class Extraction {
                    [Fluent]
                    public sealed class ExtractOptions {
                        public bool IncludeNotes { get; set; }
                        public Range Pages { get; set; } = new Range();
                    }
                }

                public class TocBlock {
                    [Fluent]
                    public sealed class Entry {
                        public string? Text { get; set; }
                        public int Level { get; set; }
                    }

                    // Not marked: stays without methods, as before.
                    public sealed class Hidden {
                        public string? Text { get; set; }
                    }
                }

                public sealed class Range {
                    public int From { get; set; }
                }

                [Fluent]
                public abstract class Shape {
                    public string? Name { get; set; }
                }

                public sealed class Box : Shape {
                }

                public sealed class Connector : Shape {
                    public Shape? StartShape { get; set; }
                    public Shape EndShape { get; } = new Box();
                }

                public static class Usage {
                    public static object[] Run() {
                        Extraction.ExtractOptions options = new Extraction.ExtractOptions().IncludeNotes().Pages(p => p.From(2));
                        TocBlock.Entry entry = new TocBlock.Entry().Text("Intro").Level(1);
                        Connector connector = new Connector().Name("c").StartShape(new Box()).StartShape(s => s.Name("start")).EndShape(s => s.Name("end"));
                        string missing;
                        try { new Connector().StartShape(s => s.Name("x")); missing = "created"; } catch (System.InvalidOperationException) { missing = "not set"; }
                        return new object[] { options.IncludeNotes, options.Pages.From, entry.Text!, entry.Level, connector.StartShape!.Name!, connector.EndShape.Name!, missing };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);
        Assembly assembly = TestCompiler.Load(compilation);

        // An abstract type is configured in place and never created.
        Assert.Equal(new object[] { true, 2, "Intro", 1, "start", "end", "not set" }, (object[])assembly.GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!);
        Assert.NotNull(assembly.GetType("Sample.ExtractionExtractOptionsFluent"));
        Assert.NotNull(assembly.GetType("Sample.TocBlockEntryFluent"));
        Assert.Null(assembly.GetType("Sample.TocBlockHiddenFluent"));
    }

    [Fact]
    public void GettersThatBuildANewObjectGetNoInPlaceOrAddMethods() {
        const string library = """
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Document {
                    private readonly Options _options = new Options();
                    private Options? _cached;
                    private readonly List<string> _parts = new List<string>();

                    public Options Cloned => _options.Clone();
                    public Options Created { get { return new Options(); } }
                    public Options Cached => _cached ??= new Options();
                    public Options Kept => _options;
                    public Options Settable { get => _options.Clone(); set => Committed = value; }
                    [Fluent]
                    public Options Wrapped => new Options(_options);
                    public Options? Committed { get; private set; }

                    // Kept objects: a local stored before it is returned, a cache-or-create helper, a branch that is sometimes new.
                    private Options? _lazy;
                    private readonly Dictionary<string, Options> _sections = new Dictionary<string, Options>();
                    public bool Detached { get; set; }
                    public Options Lazy { get { var options = _lazy ?? new Options(); _lazy = options; return options; } }
                    public Options Section => Get("main");
                    public Options Shared => Detached ? new Options() : _options;
                    private Options Get(string key) {
                        if (_sections.TryGetValue(key, out Options? found)) { return found; }
                        var created = new Options();
                        _sections[key] = created;
                        return created;
                    }

                    public List<string> Built { get { var list = new List<string>(); list.AddRange(_parts); return list; } }
                    public List<string> Collected => _parts.Count == 0 ? new List<string>() : Collect();
                    public ReadOnlyCollection<string> ReadOnly => _parts.AsReadOnly();
                    public List<string> Parts => _parts;

                    private List<string> Collect() { var result = new List<string>(_parts); return result; }

                    // Kept collections: one return is the kept list, a reassigned local, an aliased local that is stored, a
                    // method named Copy whose body returns the field, and an immutable builder, which takes added items.
                    private List<string>? _aliased;
                    public List<string> Either { get { if (Detached) { return new List<string>(); } return _parts; } }
                    public List<string> Reused { get { var list = new List<string>(); list = _parts; return list; } }
                    public List<string> Aliased { get { var list = new List<string>(); var alias = list; _aliased = alias; return list; } }
                    public List<string> Copied => Copy();
                    public ImmutableList<string>.Builder Pending { get; } = ImmutableList.CreateBuilder<string>();
                    private List<string> Copy() => _parts;

                    // Inlining a getter that builds a new object would edit a throwaway object.
                    [FluentInline]
                    public Options Inlined => new Options();
                }

                public sealed class Options {
                    private readonly Options? _source;
                    public Options() { }
                    public Options(Options source) { _source = source; }
                    public int Size { get => _source?.Size ?? _size; set { if (_source != null) { _source.Size = value; } else { _size = value; } } }
                    private int _size;
                    public Options Clone() => new Options { Size = Size };
                }

                public static class Usage {
                    public static object[] Run() {
                        Document document = new Document().Cached(o => o.Size(1)).Kept(o => o.Size(2)).Settable(o => o.Size(3)).Wrapped(o => o.Size(4)).Part("p")
                            .Lazy(o => o.Size(5)).Section(o => o.Size(6)).Either("e").Reused("r").Copied("c").Aliased("a").Pending("q");
                        Document shared = new Document().Shared(o => o.Size(7));
                        return new object[] { document.Cached.Size, document.Kept.Size, document.Committed!.Size, document.Parts.Count, document.Lazy.Size, document.Section.Size, shared.Kept.Size, document.Pending.Count };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.All(diagnostics, d => Assert.Equal(("PFF015", DiagnosticSeverity.Info), (d.Id, d.Severity)));
        Assert.Equal(new[] { "Built", "Cloned", "Collected", "Created", "Inlined", "ReadOnly" },
            diagnostics.Select(d => d.GetMessage().Split('\'')[1].Split('.')[1]).OrderBy(n => n, StringComparer.Ordinal));
        Assembly assembly = TestCompiler.Load(compilation);

        // Wrapped hands out a new wrapper that writes through to the kept options; [Fluent] asked for its method.
        // Part, Either, Reused and Copied all add to the kept list.
        Assert.Equal(new object[] { 1, 4, 3, 4, 5, 6, 7, 1 }, (object[])assembly.GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!);
        string[] names = assembly.GetType("Sample.DocumentFluent")!.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).Distinct().ToArray();
        Assert.DoesNotContain(names, name => name is "Cloned" or "Created" or "Built" or "Collected" or "ReadOnly" or "Size");
    }

    [Fact]
    public void UnusableMutatorsAndNestedTypesAreBuildErrors() {
        const string library = """
            using System.Runtime.InteropServices;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Model {
                    [FluentMutator]
                    internal void SetHost([Optional, MarshalAs(UnmanagedType.IUnknown)] object host) { }
                    [FluentMutator]
                    internal static void SetStatic(Model model) { }
                    [FluentMutator]
                    internal void SetGeneric<T>(T value) { }
                    [FluentMutator]
                    internal void SetByRef(ref int value) { }
                    [FluentMutator]
                    private void SetHidden(int value) { }
                    [FluentMutator("not valid")]
                    internal void SetNamed(int value) { }
                    [FluentMutator("Size")]
                    internal void SetSize(int value) { }
                    public void Size(int value) { }
                    [FluentMutator]
                    internal unsafe void SetBuffer(byte* buffer) { }
                    [FluentMutator]
                    internal void SetLevel(Level level) { }
                    [FluentMutator("ModelFluent")]
                    internal void SetAlias(int value) { }
                }

                // The generated methods of a sealed generic type declare T, so a value parameter can't be called T.
                [Fluent]
                public sealed class Holder<T> {
                    [FluentMutator]
                    internal void SetSize(int T) { }
                }

                internal enum Level { Low, High }

                public class Generic<T> {
                    [Fluent]
                    public sealed class Inner { public int Size { get; set; } }
                }

                internal class Private {
                    [Fluent]
                    public sealed class Inner { public int Size { get; set; } }

                    // A marker for top-level types needs no methods of its own, so it is no error.
                    [Fluent]
                    public interface IMarker { }
                }

                public sealed class Marked : Private.IMarker {
                    public int Size { get; set; }
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = Run(library);

        // Optional COM interface, static, generic, by-ref, private, badly named, pointer, internal-typed, type-parameter-named and
        // class-named mutators; Size collides with the instance method; two nested types whose containers rule out extension methods.
        Assert.Equal(new[] { "PFF002", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF013", "PFF014", "PFF014" }, diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void MutatorsStayReachableThroughOverridesHidingAndInstanceMethods() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Control {
                    public string Log = "";
                    internal bool IsResolved;

                    [FluentMutator]
                    internal virtual void SetMode(int mode) { Log += "base" + mode + ";"; }

                    // Answers Resolved() itself, so the mutator keeps a required argument.
                    public bool Resolved() => IsResolved;

                    [FluentMutator]
                    internal void SetResolved(bool value) { IsResolved = value; }
                }

                // An open derived type overriding the mutator reuses the base's generic method, which dispatches to the override.
                public class Button : Control {
                    internal override void SetMode(int mode) { Log += "button" + mode + ";"; }
                }

                public abstract class Plain {
                    public string Title = "";

                    [FluentMutator]
                    internal void SetTitle(string title) { Title = "base:" + title; }
                }

                // Hides the mutator with an unmarked method; the generated method still calls the marked one.
                [Fluent]
                public sealed class Card : Plain {
                    public new void SetTitle(string title) { Title = "hidden:" + title; }
                }

                // A type whose only fluent member is a mutator is still a settings type: inlined and configured.
                public sealed class Frame {
                    internal string Border = "";

                    [FluentMutator]
                    internal void SetBorder(string color, int width = 1) { Border = color + "/" + width; }
                }

                [Fluent]
                public sealed class Panel {
                    [FluentInline]
                    public Frame Frame { get; } = new Frame();
                    public Frame Outline { get; } = new Frame();
                }

                public static class Usage {
                    public static object[] Run() {
                        Button button = new Button().Mode(2).Resolved(true);
                        Card card = new Card().Title("x");
                        Panel panel = new Panel().Border("red").Outline(f => f.Border("blue", 2));
                        return new object[] { button.Log, button.Resolved(), card.Title, panel.Frame.Border, panel.Outline.Border };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);

        Assembly assembly = TestCompiler.Load(compilation);

        Assert.Equal(new object[] { "button2;", true, "base:x", "red/1", "blue/2" }, (object[])assembly.GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!);
        // control.Resolved() binds to the instance method, so a default would leave a switch form that never runs.
        MethodInfo resolved = assembly.GetType("Sample.ControlFluent")!.GetMethods().Single(m => m.Name == "Resolved");
        Assert.False(resolved.GetParameters()[1].HasDefaultValue);
    }

    [Fact]
    public void MutatorsTakePrecedenceAndBindTheMarkedOverload() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Shape {
                    public string Log = "";

                    [FluentMutator]
                    internal virtual void SetMode(int mode) { Log += "base" + mode + ";"; }
                }

                // An override naming its own method gets it; the base's Mode still dispatches to the override.
                public class Circle : Shape {
                    [FluentMutator("SelectMode")]
                    internal override void SetMode(int mode) { Log += "circle" + mode + ";"; }
                }

                // A handwritten extension already answers Enabled(), so the mutator's argument stays required.
                [Fluent]
                public sealed class Switch {
                    public bool On;
                    public int Level;

                    [FluentMutator]
                    internal void SetEnabled(bool value) { On = value; }
                }

                public static class SwitchExtensions {
                    public static Switch Enabled(this Switch target, int level = 0) { target.Level = level + 1; return target; }
                }

                // The dynamic mutator replaces the object property's method and is called, not the string overload.
                [Fluent]
                public sealed class Cell {
                    public string Log = "";
                    public object Data { get; set; } = "";

                    [FluentMutator]
                    internal void SetData(dynamic data) { Log += "dynamic;"; }
                    internal void SetData(string data) { Log += "string;"; }
                }

                // A mutator an inherited [FluentInline] property brings wins over the derived type's own Title property.
                public class Layout {
                    public string Title = "";

                    [FluentMutator]
                    internal void SetTitle(string title) { Title = "layout:" + title; }
                }

                public class Page {
                    [FluentInline]
                    public Layout Layout { get; } = new Layout();
                }

                [Fluent]
                public sealed class Report : Page {
                    public string Title { get; set; } = "";
                }

                // Tags() belongs to the handwritten extension; the mutator still takes one or more items, or an array.
                [Fluent]
                public sealed class Tagger {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetTags(params string[] values) { Log += string.Join(",", values) + ";"; }
                }

                public static class TaggerExtensions {
                    public static Tagger Tags(this Tagger target, params int[] values) { target.Log += "ints;"; return target; }
                }

                // A parameter named like the generic methods' type parameter; named arguments keep working.
                [Fluent]
                public class Box {
                    public int Width;

                    [FluentMutator]
                    internal void SetSize(int TSelf) { Width = TSelf; }
                }

                // Same through a generated base that inlines Layout: Memo's own Title setter doesn't win over the base's method.
                [Fluent]
                public class Sheet {
                    [FluentInline]
                    public Layout Layout { get; } = new Layout();
                }

                [Fluent]
                public sealed class Memo : Sheet {
                    public string Title { get; set; } = "";
                }

                // Jagged items: the overload taking at least one row allocates int[n][].
                [Fluent]
                public sealed class Grid {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetRows(params int[][] rows) { Log += rows.Length + ";"; }
                }

                public static class GridExtensions {
                    public static Grid Rows(this Grid target, params string[] values) { target.Log += "ext;"; return target; }
                }

                // The base's mutator stays the write path for a sealed derived type with a Level property of its own.
                [Fluent]
                public class Dial {
                    public int Value;

                    [FluentMutator]
                    internal void SetLevel(int level) { Value = level * 2; }
                }

                [Fluent]
                public sealed class Knob : Dial {
                    public int Level { get; set; }
                }

                // A delegate property's verb gives way to the mutator of the same name and type.
                [Fluent]
                public sealed class Caption {
                    [Fluent("Format")]
                    public System.Func<string, string>? Formatting { get; set; }

                    [FluentMutator]
                    internal void SetFormat(System.Func<string, string> format) { Formatting = s => "[" + format(s) + "]"; }
                }

                // An exact Mode() takes that call without stripping the mutator's defaults, so Mode(level: 3) still works.
                [Fluent]
                public sealed class Gauge {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetMode(string value = "x", int level = 1) { Log += value + level + ";"; }
                }

                public static class GaugeExtensions {
                    public static Gauge Mode(this Gauge target) { target.Log += "exact;"; return target; }
                }

                // The compiler fills [CallerMemberName] for the wrapper's caller, as it would for a direct call.
                [Fluent]
                public sealed class Probe {
                    public string Origin = "";

                    [FluentMutator]
                    internal void SetOrigin([System.Runtime.CompilerServices.CallerMemberName] string origin = "") { Origin = origin; }
                }

                public static class Probes {
                    public static string Configure() => new Probe().Origin().Origin;
                }

                // A params collection gets the same overloads as a params array when Levels() is taken through a default.
                [Fluent]
                public sealed class Meter {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetLevels(params System.Collections.Generic.List<int> levels) { Log += levels.Count + ";"; }
                }

                public static class MeterExtensions {
                    public static Meter Levels(this Meter target, string tag = "") { target.Log += "ext;"; return target; }
                }

                // The at-least-one-item overload passes a typed collection, so the unmarked int[] overload can't take the call.
                [Fluent]
                public sealed class Series {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetValues(params System.Collections.Generic.IEnumerable<int> values) { Log += "marked;"; }
                    internal void SetValues(int[] values) { Log += "array;"; }
                }

                public static class SeriesExtensions {
                    public static Series Values(this Series target, string tag = "") => target;
                }

                // Items that are rectangular arrays keep their type: int[][,], not int[,][].
                [Fluent]
                public sealed class Plane {
                    public string Log = "";

                    [FluentMutator]
                    internal void SetCells(params int[][,] cells) { Log += cells.Length + ";"; }
                }

                public static class PlaneExtensions {
                    public static Plane Cells(this Plane target, params string[] values) { target.Log += "ext;"; return target; }
                }

                // Only the override inlines Layout, and a generated base covers it: Letter keeps its own Title setter.
                [Fluent]
                public class Folder {
                    public virtual Layout Layout { get; } = new Layout();
                }

                [Fluent]
                public sealed class Letter : Folder {
                    [FluentInline]
                    public override Layout Layout => base.Layout;
                    public string Title { get; set; } = "";
                }

                // A generic extension the receiver can't infer never answers Lit(), so the switch form stays.
                [Fluent]
                public sealed class Lamp {
                    public bool On;

                    [FluentMutator]
                    internal void SetLit(bool value) { On = value; }
                }

                public static class LampExtensions {
                    public static Lamp Lit<T>(this Lamp target, int level = 0) => target;
                }

                // Tuple element names don't make the mutator a second method beside the property's.
                [Fluent]
                public sealed class Spot {
                    public (int X, int Y) At { get; set; }

                    [FluentMutator]
                    internal void SetAt((int Left, int Top) at) { At = (at.Left * 10, at.Top); }
                }

                public static class Usage {
                    public static object[] Run() {
                        Circle circle = new Circle().Mode(1).SelectMode(2);
                        Switch light = new Switch().Enabled().Enabled(true);
                        Report report = new Report().Title("x");
                        Tagger tagger = new Tagger().Tags().Tags("a").Tags("b", "c").Tags(new[] { "d" });
                        return new object[] { circle.Log, light.Level, light.On, new Cell().Data("x").Log, report.Layout.Title, report.Title,
                            tagger.Log, new Box().Size(TSelf: 3).Width, new Spot().At((1, 2)).At.X,
                            new Memo().Title("y").Layout.Title, new Grid().Rows().Rows(new[] { 1 }, new[] { 2 }).Log, new Lamp().Lit().On,
                            new Plane().Cells().Cells(new int[1, 1], new int[2, 2]).Log, new Letter().Title("z").Title, new Knob().Level(3).Value,
                            new Caption().Format(s => s + "!").Formatting!("a"), new Gauge().Mode().Mode(level: 3).Log, Probes.Configure(),
                            new Meter().Levels().Levels(3, 4).Levels(new System.Collections.Generic.List<int> { 5 }).Log,
                            new Series().Values(1, 2).Log };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);

        Assert.Equal(new object[] { "circle1;circle2;", 1, true, "dynamic;", "layout:x", "", "ints;a;b,c;d;", 3, 10, "layout:y", "ext;2;", true, "ext;2;", "z", 6, "[a!]", "exact;x3;", "Configure", "ext;2;1;", "marked;" }, Execute(compilation));
    }

    [Fact]
    public void GettersThatMayHandOutTheKeptObjectKeepTheirMethods() {
        const string library = """
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public class Board {
                    private List<string> _items = new List<string>();

                    // Stored through extra parentheses before it is returned.
                    public List<string> Items { get { var list = new List<string>(); _items = ((list)); return list; } }

                    // An overridable helper: a derived type can return its kept list.
                    public List<string> Tags => ReadTags();

                    protected virtual List<string> ReadTags() => new List<string>();

                    public int Count => _items.Count;

                    // Stored on one branch of a conditional.
                    public bool Keep = true;
                    private List<string> _notes = new List<string>();
                    public List<string> Notes { get { var list = new List<string>(); _notes = Keep ? list : _notes; return list; } }
                    public int NoteCount => _notes.Count;

                    // Stored through a switch expression arm.
                    private List<string> _marks = new List<string>();
                    public List<string> Marks { get { var list = new List<string>(); _marks = Keep switch { true => list, false => _marks }; return list; } }
                    public int MarkCount => _marks.Count;

                    // A user-defined conversion hands out the cached settings, so Options(o => ...) stays.
                    public Settings Options => (Settings)new Token();
                }

                public sealed class Token { }

                public sealed class Settings {
                    public static readonly Settings Cached = new Settings();
                    public int Value { get; set; }
                    public static explicit operator Settings(Token token) => Cached;
                }

                public sealed class Pinboard : Board {
                    private readonly List<string> _tags = new List<string>();

                    protected override List<string> ReadTags() => _tags;
                }

                // A virtual getter: the override hands out its kept list.
                [Fluent]
                public class Shelf {
                    public virtual List<string> Books => new List<string>();
                }

                public sealed class Library : Shelf {
                    private readonly List<string> _books = new List<string>();

                    public override List<string> Books => _books;
                }

                public static class Usage {
                    public static object[] Run() {
                        Pinboard board = new Pinboard().Item("a").Tag("b").Note("n").Mark("m").Options(o => o.Value = 7);
                        return new object[] { board.Count, board.Tags.Count, new Library().Book("x").Books.Count, board.NoteCount, board.MarkCount, Settings.Cached.Value };
                    }
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);

        Assert.Equal(new object[] { 1, 1, 1, 1, 1, 7 }, Execute(compilation));
    }

    [Fact]
    public void ADerivedTypeGeneratesTheMutatorAPrefixedBaseLeftOut() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                // Prefixed naming generates Height(int) and Level(int) for the properties, but no mutator methods.
                [Fluent]
                public class Shape {
                    public int Height { get; set; }
                    public int Level { get; set; }
                    public int Stored;

                    [FluentMutator]
                    public void SetLevel(int level) { Stored = level; }
                }

                public sealed class Frame {
                    public int Size { get; set; }
                    public int Stored;

                    [FluentMutator]
                    public void SetDepth(int depth) { Stored = depth; }
                }

                // Prefixed naming generates no inline methods either.
                [Fluent]
                public class Board {
                    [FluentInline]
                    public Frame Frame { get; } = new Frame();
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) =
            TestCompiler.Run(baseLibrary, LanguageVersion.Latest, new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" }, new FluentGenerator());
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string library = """
            using PowerForge.Fluent;
            using Sample.Blocks;

            namespace Sample {
                [Fluent]
                public sealed class Tile : Shape {
                }

                // Board's inlined SetDepth produced no Depth(int), so Wall keeps its own Depth setter.
                [Fluent]
                public sealed class Wall : Board {
                    public int Depth { get; set; }
                }

                public static class Usage {
                    public static object[] Run() { Tile tile = new Tile().Level(3); return new object[] { tile.Stored, tile.Level, new Wall().Depth(4).Depth }; }
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(library, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);

        Assembly referenced = Assembly.Load(image.ToArray());
        ResolveEventHandler resolve = (_, args) => args.Name == referenced.FullName ? referenced : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try {
            // The base generated no Level method, so Tile gets one, and it calls the inherited mutator.
            Assert.Equal(new object[] { 3, 0, 4 }, Execute(compilation));
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void AReferencedBaseNamedLikeItsNamespaceStillCoversItsMutators() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Reports {
                // Reports.ReportsFluent is both the convention class and the Prefixed namespace class name.
                [Fluent]
                public class Reports {
                    public string Stored = "";

                    [FluentMutator]
                    public void SetTitle(string title) { Stored = title; }
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        // The same coincidence under Prefixed naming: AddTitle there is the Titles collection helper, not the mutator.
        const string prefixedLibrary = """
            using System.Collections.Generic;
            using PowerForge.Fluent;

            namespace Sample.Notes {
                [Fluent]
                public class Notes {
                    public List<string> Titles { get; } = new List<string>();

                    [FluentMutator("AddTitle")]
                    public void InsertTitle(string title) { Titles.Add(title.Trim()); }
                }
            }
            """;
        (Compilation prefixedCompilation, ImmutableArray<Diagnostic> prefixedDiagnostics) =
            TestCompiler.Run(prefixedLibrary, LanguageVersion.Latest, new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" }, new FluentGenerator());
        Assert.Empty(prefixedDiagnostics);
        using MemoryStream prefixedImage = new MemoryStream();
        Assert.True(prefixedCompilation.WithAssemblyName("Notes").Emit(prefixedImage).Success);

        const string library = """
            using PowerForge.Fluent;
            using Sample.Reports;

            namespace Sample {
                [Fluent]
                public class ReportPreview : Sample.Reports.Reports {
                    public int Zoom { get; set; }
                }

                // The derived type generates its own AddTitle, which calls the mutator.
                [Fluent]
                public sealed class NotePreview : Sample.Notes.Notes {
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new ReportPreview().Title("x").Zoom(2).Stored, new NotePreview().AddTitle(" a ").Titles[0] };
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(library, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()), MetadataReference.CreateFromImage(prefixedImage.ToArray()) }, new FluentGenerator());
        // The base's generic Title covers the derived type, so generating it again would be PFF002.
        Assert.Empty(diagnostics);

        Assembly referenced = Assembly.Load(image.ToArray());
        Assembly notes = Assembly.Load(prefixedImage.ToArray());
        ResolveEventHandler resolve = (_, args) => args.Name == referenced.FullName ? referenced : args.Name == notes.FullName ? notes : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try {
            Assert.Equal(new object[] { "x", "a" }, Execute(compilation));
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void SuppressedNullDefaultsKeepTheirNonNullableType() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Tag {
                    public string Label { get; private set; } = "";

                    [FluentMutator]
                    internal void SetLabel(string label = null!) { Label = label ?? "none"; }

                    [FluentMutator]
                    internal void SetData(dynamic? data) { }

            #nullable disable
                    [FluentMutator]
                    internal void SetBlob(dynamic blob = null) { }
            #nullable restore
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Tag().Label().Label, new Tag().Label("a").Label };
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);

        // The generated calls must not pass a string? to the non-nullable parameter (CS8604) or cast a dynamic? to object (CS8600).
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.Equal(new object[] { "none", "a" }, Execute(compilation));
    }

    [Fact]
    public void NullDefaultsCompileWithoutWarningsWhenTheProjectDisablesNullable() {
        const string library = """
            using System.Runtime.InteropServices;
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Note {
                    public string Text { get; private set; } = "";

                    [FluentMutator]
                    internal void SetText([Optional] object tag, string text = null) { Text = (text ?? "none") + (tag == null ? "" : "+tag"); }
                }

                public static class Usage {
                    // An omitted [Optional] object is Type.Missing, as in a direct SetText() call; an explicit null stays null.
                    public static object[] Run() => new object[] { new Note().Text().Text, new Note().Text(null).Text, new Note().Text(1, "a").Text };
                }
            }
            """;

        (Compilation generated, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);
        Compilation compilation = generated.WithOptions(((CSharpCompilationOptions)generated.Options).WithNullableContextOptions(NullableContextOptions.Disable));

        // Projects building with TreatWarningsAsErrors must not get CS8625 from `string text = null` in the generated file.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.Equal(new object[] { "none+tag", "none", "a+tag" }, Execute(compilation));
    }

    [Fact]
    public void PrefixedNamingStillIgnoresNestedTypes() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                public class Outer {
                    [Fluent]
                    public class Inner { public int Size { get; set; } }
                }

                [Fluent]
                public class Top { public int Size { get; set; } }

                // Prefixed naming keeps its exact Visible() beside a handwritten optional extension, and IsShown for a bool?.
                [Fluent]
                public sealed class Widget {
                    public bool IsVisible { get; set; }
                    public bool? IsShown { get; set; }
                    public string Style = "";
                }

                public static class WidgetExtensions {
                    public static Widget Visible(this Widget target, string style = "plain") { target.Style = style; return target; }
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Top().Size(3).Size, new Widget().Visible().IsVisible, new Widget().IsShown(true).IsShown! };
                }
            }
            """;

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) =
            TestCompiler.Run(library, LanguageVersion.Latest, new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" }, new FluentGenerator());
        Assert.Empty(diagnostics);

        // Both types would put a generic Size method into one SampleFluent class.
        Assert.Equal(new object[] { 3, true, true }, Execute(compilation));
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, LanguageVersion version = LanguageVersion.Latest) =>
        TestCompiler.Run(source, version, null, new FluentGenerator());

    private static object[] Execute(Compilation compilation) => (object[])TestCompiler.Load(compilation).GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!;
}
