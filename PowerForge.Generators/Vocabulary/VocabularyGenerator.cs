using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PowerForge.Generators.Fluent;

namespace PowerForge.Generators.Vocabulary {
    /// <summary>
    /// Emits the shared vocabulary (<c>ThemeMode</c>, <c>Tone</c>, <c>Icon</c>) into a library's own namespace from one
    /// definition, so every engine spells them the same way without a runtime dependency. A project lists the types it
    /// adopts in <c>PowerForgeVocabulary</c>; <c>PowerForgeVocabularyNamespace</c> (default: the root namespace) places them.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class VocabularyGenerator : IIncrementalGenerator {
        private const string DefinitionNamespace = "namespace PowerForge.Vocabulary {";

        private static readonly string[] Known = { "ThemeMode", "Tone", "Icon" };

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            IncrementalValueProvider<(string Types, string Namespace)> settings = context.AnalyzerConfigOptionsProvider.Select((options, _) => Read(options.GlobalOptions));
            context.RegisterSourceOutput(settings, Emit);
        }

        private static (string Types, string Namespace) Read(AnalyzerConfigOptions options) {
            options.TryGetValue("build_property.PowerForgeVocabulary", out string? types);
            options.TryGetValue("build_property.PowerForgeVocabularyNamespace", out string? ns);
            if (string.IsNullOrWhiteSpace(ns)) {
                options.TryGetValue("build_property.RootNamespace", out ns);
            }
            return (types?.Trim() ?? string.Empty, ns?.Trim() ?? string.Empty);
        }

        private static void Emit(SourceProductionContext output, (string Types, string Namespace) settings) {
            List<string> requested = FluentModel.ListSetting(settings.Types);
            if (requested.Count == 0) {
                return;
            }
            bool validNamespace = settings.Namespace.Length > 0 && settings.Namespace.Split('.').All(part => SyntaxFacts.IsValidIdentifier(part));
            if (!validNamespace) {
                output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidVocabulary, Location.None, settings.Namespace, "PowerForgeVocabularyNamespace"));
                return;
            }
            foreach (string name in requested.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string? known = Known.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
                if (known == null) {
                    output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidVocabulary, Location.None, name, "PowerForgeVocabulary (" + string.Join(", ", Known) + ")"));
                    continue;
                }
                string source = Definition(known).Replace(DefinitionNamespace, "namespace " + settings.Namespace + " {");
                output.AddSource("PowerForge.Vocabulary." + known + ".g.cs", source);
            }
        }

        // The definition file, embedded as a resource so it stays ordinary, highlighted C#.
        private static string Definition(string name) {
            string resource = "PowerForge.Generators.Vocabulary." + name + ".g.cs";
            using Stream stream = typeof(VocabularyGenerator).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The generator assembly is missing its embedded resource " + resource + ".");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
