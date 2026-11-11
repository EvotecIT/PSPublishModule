using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Generators.Vocabulary;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>The shared vocabulary lands in the library's namespace, only for the types it asks for, and compiles as C# 7.3.</summary>
public sealed class VocabularyGeneratorTests {
    private const string Usage = @"
        namespace Sample.Charts {
            public static class Usage {
                public static object[] Run() {
                    Tone parsed = Tone.Parse(""Warning"");
                    Tone named;
                    bool urlIsTone = Tone.TryParse(""https://example.com"", out named);
                    Icon icon = Icon.Named(""Tabler:Server"");
                    Icon url;
                    bool urlIsIcon = Icon.TryParse(""https://example.com/a.svg"", out url);
                    return new object[] { ThemeMode.Dark.ToString(), parsed == Tone.Warning, parsed.IsNamed, Tone.Named(""brand"").IsNamed, Tone.None.Name,
                        urlIsTone, icon.Key, icon == Icon.Named(""tabler:server""), urlIsIcon, Icon.None.IsEmpty };
                }
            }
        }";

    [Theory]
    [InlineData(LanguageVersion.Latest)]
    [InlineData(LanguageVersion.CSharp7_3)]
    public void RequestedTypesAreEmittedIntoTheLibraryNamespace(LanguageVersion version) {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run(Usage, version,
            new Dictionary<string, string> { ["PowerForgeVocabulary"] = "ThemeMode; Tone; Icon", ["RootNamespace"] = "Sample.Charts" }, new VocabularyGenerator());
        Assert.Empty(diagnostics);

        object[] result = (object[])TestCompiler.Load(compilation).GetType("Sample.Charts.Usage")!.GetMethod("Run")!.Invoke(null, null)!;

        Assert.Equal(new object[] { "Dark", true, false, true, "none", false, "tabler:server", true, false, true }, result);
    }

    [Fact]
    public void OnlyRequestedTypesAreEmittedAndUnknownOnesAreBuildErrors() {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = TestCompiler.Run("namespace Sample { }", LanguageVersion.Latest,
            new Dictionary<string, string> { ["PowerForgeVocabulary"] = "Tone;Colour", ["PowerForgeVocabularyNamespace"] = "Sample" }, new VocabularyGenerator());

        Assert.Equal("PFF008", Assert.Single(diagnostics).Id);
        Assert.NotNull(compilation.GetTypeByMetadataName("Sample.Tone"));
        Assert.Null(compilation.GetTypeByMetadataName("Sample.ThemeMode"));
    }
}
