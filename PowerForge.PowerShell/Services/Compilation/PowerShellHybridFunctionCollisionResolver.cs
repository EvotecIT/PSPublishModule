using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Keeps PowerShell's function replacement semantics when a hybrid module defines one command name more than once.
/// </summary>
internal static class PowerShellHybridFunctionCollisionResolver
{
    internal static PowerShellTypedCompilationResult RouteNameCollisionsToFallback(
        PowerShellTypedCompilationResult typed,
        string? targetFramework)
    {
        var definitions = new List<(string Path, ScriptBlockAst Root, FunctionDefinitionAst Function)>();
        var sources = new List<(string Path, ScriptBlockAst Root)>();
        foreach (var sourcePath in typed.SourcePaths)
        {
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
            if (errors.Length > 0)
                return typed;
            sources.Add((sourcePath, ast));
            definitions.AddRange(ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(function => (sourcePath, ast, function)));
        }

        var duplicateNames = definitions
            .GroupBy(static item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var earlyAvailabilityNames = definitions
            .Where(definition => sources.Any(source => source.Root.FindAll(
                    node => node is CommandAst command &&
                            (!PowerShellCompilationPathSafety.PathEquals(source.Path, definition.Path) ||
                             command.Extent.StartOffset < definition.Function.Extent.StartOffset) &&
                            IsModuleScope(command, source.Root),
                    searchNestedScriptBlocks: true)
                .OfType<CommandAst>()
                .Any(command => command.GetCommandName()?.Equals(definition.Function.Name, StringComparison.OrdinalIgnoreCase) == true)))
            .Select(static definition => definition.Function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackNames = duplicateNames.Concat(earlyAvailabilityNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedMethods = typed.Methods
            .Where(method => fallbackNames.Contains(method.SourceName))
            .Select(method => Path.GetFullPath(string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath) + "\0" + method.SourceName + "\0" + method.SourceLine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedMethods.Count == 0)
            return typed;

        var excludedNames = typed.Methods
            .Where(method => fallbackNames.Contains(method.SourceName))
            .Select(static method => method.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePaths,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            excludedMethods,
            PowerShellCompilationCapabilities.BinaryModule);
        var diagnostics = excludedNames.Select(name =>
        {
            var definition = definitions
                .Where(item => item.Function.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static item => item.Function.Extent.StartOffset)
                .First();
            var message = duplicateNames.Contains(name)
                ? $"Function '{name}' has multiple retained definitions, so hybrid compilation keeps PowerShell's runtime replacement semantics."
                : $"Function '{name}' is referenced by retained module-scope code before or across a separately loaded declaration boundary, so hybrid compilation preserves PowerShell's command-availability timing.";
            return new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                message,
                definition.Path,
                definition.Function.Extent.StartLineNumber,
                definition.Function.Extent.StartColumnNumber);
        });
        return new PowerShellTypedCompilationResult(
            filtered.SourcePath,
            filtered.NamespaceName,
            filtered.TypeName,
            filtered.SourceCode,
            filtered.Methods,
            filtered.Diagnostics.Concat(diagnostics)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            filtered.SourcePaths);
    }

    private static bool IsModuleScope(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, root))
                return false;
        }
        return true;
    }
}
