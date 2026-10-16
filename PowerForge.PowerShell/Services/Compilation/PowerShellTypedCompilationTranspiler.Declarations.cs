using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellTypedCompilationTranspiler
{
    private static void ApplyDeclarationSourceContext(List<ParsedSource> parsedFiles,
        PowerShellCompilationCapability capabilities, string? guardedExecutableEntryPath,
        IReadOnlyCollection<string>? nativeDeclarationTimingSourcePaths, bool nativeScriptEntry)
    {
        var declarationPaths = nativeDeclarationTimingSourcePaths is null
            ? PowerShellHybridFunctionCollisionResolver.FindGuardedExecutableDeclarationPaths(
                parsedFiles.Select(static file => file.Document), guardedExecutableEntryPath, capabilities)
            : new HashSet<string>(nativeDeclarationTimingSourcePaths, PowerShellCompilationPathSafety.PathComparer);
        if (capabilities != PowerShellCompilationCapabilities.HybridExecutable) declarationPaths.Clear();
        else if (nativeScriptEntry && !string.IsNullOrWhiteSpace(guardedExecutableEntryPath))
        {
            var entry = Path.GetFullPath(guardedExecutableEntryPath!);
            if (parsedFiles.Any(file => PowerShellCompilationPathSafety.PathEquals(file.Path, entry))) declarationPaths.Add(entry);
        }
        for (var index = 0; index < parsedFiles.Count; index++)
        {
            var file = parsedFiles[index];
            if (capabilities == PowerShellCompilationCapabilities.HybridExecutable &&
                !string.IsNullOrWhiteSpace(guardedExecutableEntryPath))
                file = new ParsedSource(file.Path, file.Ast, file.Plan, file.Document.WithPackagedPathGuards());
            if (declarationPaths.Contains(file.Path))
                file = new ParsedSource(file.Path, file.Ast, file.Plan, file.Document.WithFunctionDeclarationTiming());
            parsedFiles[index] = file;
        }
    }

    private static FunctionSource? CreateFunctionSource(ParsedSource parsed,
        PowerShellRootFunctionDeclaration declaration, PowerShellCompilationCapability capabilities)
    {
        var function = declaration.Syntax;
        var occurrence = parsed.Document.RootFunctionDeclarations.TakeWhile(candidate => !ReferenceEquals(candidate, declaration))
            .Count(candidate => (!candidate.IsConditional || candidate.UsesConditionalNativeBinding(capabilities)) &&
                                candidate.Syntax.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase) &&
                                candidate.Syntax.Body.Extent.StartLineNumber == function.Body.Extent.StartLineNumber);
        var unit = parsed.Plan.Units.Where(candidate => candidate.Kind == PowerShellCompilationUnitKind.Function &&
                candidate.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase) &&
                candidate.StartLine == function.Body.Extent.StartLineNumber)
            .Skip(occurrence).FirstOrDefault();
        return unit is null ? null : new FunctionSource(parsed, function, unit,
            declaration.GetBindingName(parsed.Document, capabilities),
            declaration.UsesConditionalNativeBinding(capabilities) ? function.Extent.StartOffset : null);
    }

    private static string GetMethodKey(string sourcePath, string name, int sourceLine, int? conditionalDeclarationOffset = null)
        => PowerShellHybridModuleComposer.GetCompiledMethodKey(sourcePath, name, sourceLine, conditionalDeclarationOffset);

    private static string GetMethodKey(FunctionSource source)
        => GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber,
            source.ConditionalDeclarationOffset);

    private static string ResolveCollisionFreeTypeName(string requestedTypeName, IEnumerable<ScriptBlockAst> asts)
    {
        var generatedMethods = asts.SelectMany(ast => ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>())
            .Select(static function => PowerShellCSharpSymbolRenderer.Identifier(function.Name))
            .ToHashSet(StringComparer.Ordinal);
        var candidate = PowerShellCSharpSymbolRenderer.Identifier(requestedTypeName);
        while (generatedMethods.Contains(candidate))
            candidate = PowerShellCSharpSymbolRenderer.Identifier("_" + candidate.TrimStart('@'));
        return candidate;
    }

    private sealed class FunctionSource
    {
        internal FunctionSource(ParsedSource parsed, FunctionDefinitionAst function, PowerShellCompilationUnitPlan unit,
            string? bindingName = null, int? conditionalDeclarationOffset = null)
        {
            Parsed = parsed;
            Function = function;
            Unit = unit;
            BindingName = bindingName ?? function.Name;
            ConditionalDeclarationOffset = conditionalDeclarationOffset;
        }

        internal ParsedSource Parsed { get; }
        internal FunctionDefinitionAst Function { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
        internal string BindingName { get; }
        internal int? ConditionalDeclarationOffset { get; }
    }

    private sealed class ParsedSource
    {
        internal ParsedSource(string path, ScriptBlockAst ast, PowerShellCompilationFilePlan plan, ParsedSourceDocument document)
        {
            Path = path;
            Ast = ast;
            Plan = plan;
            Document = document;
        }

        internal string Path { get; }
        internal ScriptBlockAst Ast { get; }
        internal PowerShellCompilationFilePlan Plan { get; }
        internal ParsedSourceDocument Document { get; }
    }
}
