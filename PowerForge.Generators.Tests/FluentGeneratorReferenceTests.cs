using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Generators.Fluent;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>
/// Consumers of a referenced library: inlined settings, forwards and preset names that cross an assembly boundary.
/// </summary>
public sealed class FluentGeneratorReferenceTests {
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

    [Fact]
    public void ReferencedForwardsToInternalHelpersReachTheOwner() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                [Fluent]
                [FluentForward("Content", typeof(ContentBuilder), "Replace")]
                public sealed class ChartOptions {
                    public string? Content { get; set; }
                }

                // No fluent methods are generated for this type, so an owner keeps its plain Border(value).
                [FluentForward("Border", typeof(ContentBuilder), "Frame")]
                public sealed class FrameOptions {
                    public string? Border { get; set; }
                }

                internal static class ContentBuilder {
                    internal static void Replace(ChartOptions options, string text) => options.Content = "text:" + text;
                    internal static void Frame(FrameOptions options, string border) => options.Border = "frame:" + border;
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string library = """
            using PowerForge.Fluent;
            using Sample.Blocks;

            namespace Sample {
                [Fluent]
                public sealed class Card {
                    [FluentInline]
                    public ChartOptions Options { get; } = new();
                    [FluentInline]
                    public FrameOptions Frame { get; } = new();
                }

                public static class Usage {
                    public static object[] Run() {
                        Card card = new Card().Content("a").Border("thin");
                        return new object[] { card.Options.Content!, card.Frame.Border! };
                    }
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
            // Content(...) calls the forward through the extension the referenced library generated.
            Assert.Equal(new object[] { "text:a", "thin" }, Execute(compilation));
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void ReferencedNonPublicMethodsLeavePresetNamesFree() {
        const string baseLibrary = @"
            namespace Sample.Bases {
                public abstract class Panel {
                    protected internal void Reset(int size = 10) { }
                    internal void Clear() { }
                }
            }";
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string derivedLibrary = @"
            using PowerForge.Fluent;
            using Sample.Bases;

            namespace Sample {
                [Fluent]
                public sealed class Card : Panel {
                    [FluentPreset(""Reset"", 0)]
                    [FluentPreset(""Clear"", -1)]
                    public int Size { get; set; } = 5;
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Card().Reset().Size, new Card().Clear().Size };
                }
            }";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(derivedLibrary, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GenericMethodsLeavePresetNamesFree() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Card {
                    // card.Reset() and card.Clear() cannot infer T, so they bind to the generated presets.
                    public Card Reset<T>(int size = 10) { Size = size; return this; }
                    public Card Clear<T>() { Size = 99; return this; }
                    [FluentPreset("Reset", 0)]
                    [FluentPreset("Clear", -1)]
                    public int Size { get; set; } = 5;
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Card().Reset().Size, new Card().Clear().Size };
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        Assert.Empty(diagnostics);
        Assert.Equal(new object[] { 0, -1 }, Execute(compilation));
    }

    [Fact]
    public void ReferencedForwardsRecoverEachInternalOverload() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                [Fluent]
                [FluentForward("Content", typeof(ContentBuilder), "Replace")]
                public sealed class ChartOptions {
                    public string? Content { get; set; }
                }

                public static class ContentBuilder {
                    public static void Replace(ChartOptions options, string text) => options.Content = "text:" + text;
                    internal static void Replace(ChartOptions options, int number) => options.Content = "number:" + number;
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string library = """
            using PowerForge.Fluent;
            using Sample.Blocks;

            namespace Sample {
                [Fluent]
                public sealed class Card {
                    [FluentInline]
                    public ChartOptions Options { get; } = new();
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Card().Content("a").Options.Content!, new Card().Content(3).Options.Content! };
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
            // The public overload is called directly, the internal one through the referenced library's generated extension.
            Assert.Equal(new object[] { "text:a", "number:3" }, Execute(compilation));
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void StaticConstPresetsConvertLikeConstants() {
        const string library = """
            using PowerForge.Fluent;

            namespace Sample {
                [Fluent]
                public sealed class Gauge {
                    [FluentPreset("Small", typeof(Sizes), nameof(Sizes.Small))]
                    [FluentPreset("Huge", typeof(Sizes), nameof(Sizes.Huge))]
                    public byte Size { get; set; }
                    [FluentPreset("Unset", typeof(Sizes), nameof(Sizes.Zero))]
                    [FluentPreset("Wide", typeof(Sizes), nameof(Sizes.Wide))]
                    [FluentPreset("Other", typeof(Sizes), nameof(Sizes.Other))]
                    public Mode Mode { get; set; } = Mode.One;
                    [FluentPreset("Loose", typeof(Sizes), nameof(Sizes.Loose))]
                    public byte Count { get; set; }
                }

                public enum Mode { One = 1 }
                public enum Shape { Zero }

                public static class Sizes {
                    public const int Small = 1;
                    public const int Huge = 300;
                    public const long Zero = 0;
                    public const int Wide = 1;
                    public const Shape Other = Shape.Zero;
                    public static readonly int Loose = 1;
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(library);
        // Huge does not fit a byte, Wide is not zero, an enum constant needs the same enum and a readonly field is no constant.
        Assert.Equal(new[] { "PFF010", "PFF010", "PFF010", "PFF010" }, diagnostics.Select(d => d.Id));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));

        Compilation usage = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Sample {
                public static class Usage {
                    public static object[] Run() {
                        Gauge gauge = new Gauge().Small().Unset();
                        return new object[] { gauge.Size, (int)gauge.Mode };
                    }
                }
            }
            """, (CSharpParseOptions)compilation.SyntaxTrees.First().Options));
        Assert.Equal(new object[] { (byte)1, 0 }, Execute(usage));
    }

    [Fact]
    public void ReferencedSettingsWithAPublicCopyMethodAreConfiguredOnACopy() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                [Fluent(Copy = "Clone")]
                public sealed class Shared {
                    public string Name { get; set; } = "first";
                    public Shared Clone() => (Shared)MemberwiseClone();
                }

                [Fluent(Copy = "Clone")]
                public sealed class Hidden {
                    public string Name { get; set; } = "first";
                    internal Hidden Clone() => (Hidden)MemberwiseClone();
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string consumer = """
            using System;
            using Sample.Blocks;

            namespace Sample {
                [PowerForge.Fluent.Fluent]
                public sealed class Canvas {
                    public Shared Shared { get; set; } = new Shared();
                    public Hidden Hidden { get; set; } = new Hidden();
                }

                public static class Usage {
                    public static object[] Run() {
                        Canvas canvas = new Canvas();
                        Shared? seen = null;
                        Hidden? hidden = null;
                        canvas.Shared(s => { seen = s; s.Name = "second"; }).Hidden(h => { hidden = h; h.Name = "second"; });
                        seen!.Name = "late";
                        try {
                            canvas.Shared(s => { s.Name = "partial"; throw new InvalidOperationException(); });
                        } catch (InvalidOperationException) {
                        }
                        return new object[] { canvas.Shared.Name, ReferenceEquals(hidden, canvas.Hidden), canvas.Hidden.Name };
                    }
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(consumer, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);

        // The public Clone is used across the assembly boundary; the internal one is not visible, so Hidden is edited in place.
        Assembly blocks = Assembly.Load(image.ToArray());
        ResolveEventHandler resolve = (_, args) => args.Name == blocks.FullName ? blocks : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try {
            Assert.Equal(new object[] { "second", true, "second" }, Execute(compilation));
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void DerivableTypesReuseTheMethodsOfAReferencedGeneratedBase() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Charts {
                public sealed class ChartOptions {
                    public string Theme { get; set; } = "light";
                }

                [Fluent]
                public class Chart {
                    public string? Title { get; set; }
                    [FluentInline]
                    public ChartOptions Options { get; } = new ChartOptions();
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string consumer = """
            namespace Sample.Charts {
                public class Pie : Chart {
                    public int Slices { get; set; }
                }

                public sealed class Donut : Pie {
                    public int Hole { get; set; }
                }
            }

            namespace Sample {
                using Sample.Charts;

                public static class Usage {
                    public static object[] Run() {
                        Pie pie = new Pie().Title("Share").Theme("dark").Slices(3);
                        Donut donut = new Donut().Theme("muted").Hole(2).Title("Ring").Slices(4);
                        return new object[] { pie.Title!, pie.Options.Theme, pie.Slices, donut.Title!, donut.Options.Theme, donut.Hole, donut.Slices };
                    }
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(consumer, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);
        Assert.Equal(new object[] { "Share", "dark", 3, "Ring", "muted", 2, 4 }, ExecuteWith(compilation, image.ToArray()));
    }

    [Fact]
    public void ReferencedPresetsBackedByInternalMembersReachTheOwner() {
        const string baseLibrary = """
            using PowerForge.Fluent;

            namespace Sample.Blocks {
                [Fluent]
                public sealed class BlockOptions {
                    [FluentPreset("Standard", typeof(Defaults), "Standard")]
                    public string Theme { get; set; } = "light";
                }

                internal static class Defaults {
                    internal static string Standard => "standard";
                }
            }
            """;
        (Compilation baseCompilation, ImmutableArray<Diagnostic> baseDiagnostics) = Run(baseLibrary);
        Assert.Empty(baseDiagnostics);
        using MemoryStream image = new MemoryStream();
        Assert.True(baseCompilation.Emit(image).Success);

        const string consumer = """
            using PowerForge.Fluent;
            using Sample.Blocks;

            namespace Sample {
                [Fluent]
                public sealed class Card {
                    [FluentInline]
                    public BlockOptions Options { get; } = new BlockOptions();
                }

                public static class Usage {
                    public static object[] Run() => new object[] { new Card().Standard().Options.Theme };
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(consumer, LanguageVersion.Latest, null,
            new[] { MetadataReference.CreateFromImage(image.ToArray()) }, new FluentGenerator());
        Assert.Empty(diagnostics);
        Assert.Equal(new object[] { "standard" }, ExecuteWith(compilation, image.ToArray()));
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, LanguageVersion version = LanguageVersion.Latest, bool prefixed = false) =>
        TestCompiler.Run(source, version, prefixed ? new Dictionary<string, string> { ["PowerForgeFluentNaming"] = "Prefixed" } : null, new FluentGenerator());

    private static object[] Execute(Compilation compilation) => (object[])TestCompiler.Load(compilation).GetType("Sample.Usage")!.GetMethod("Run")!.Invoke(null, null)!;

    // Runs the consumer with the referenced library it was compiled against.
    private static object[] ExecuteWith(Compilation compilation, byte[] library) {
        Assembly referenced = Assembly.Load(library);
        ResolveEventHandler resolve = (_, args) => args.Name == referenced.FullName ? referenced : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try {
            return Execute(compilation);
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }
}
