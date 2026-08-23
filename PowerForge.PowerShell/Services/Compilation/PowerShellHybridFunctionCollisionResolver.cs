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
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(typed.SourcePath, out tokens, out errors);
        if (errors.Length > 0)
            return typed;

        var duplicateNames = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedMethods = typed.Methods
            .Where(method => duplicateNames.Contains(method.SourceName))
            .Select(static method => method.SourceName + "\0" + method.SourceLine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedMethods.Count == 0)
            return typed;

        var excludedNames = typed.Methods
            .Where(method => duplicateNames.Contains(method.SourceName))
            .Select(static method => method.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePath,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            excludedMethods);
        var diagnostics = excludedNames.Select(name =>
        {
            var definition = ast.FindAll(
                    node => node is FunctionDefinitionAst function && function.Name.Equals(name, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .OrderBy(static function => function.Extent.StartOffset)
                .First();
            return new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Function '{name}' has multiple retained definitions, so hybrid compilation keeps PowerShell's runtime replacement semantics.",
                typed.SourcePath,
                definition.Extent.StartLineNumber,
                definition.Extent.StartColumnNumber);
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
                .ToArray());
    }
}
