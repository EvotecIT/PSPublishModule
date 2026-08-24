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
        var definitions = new List<(string Path, FunctionDefinitionAst Function)>();
        foreach (var sourcePath in typed.SourcePaths)
        {
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
            if (errors.Length > 0)
                return typed;
            definitions.AddRange(ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(function => (sourcePath, function)));
        }

        var duplicateNames = definitions
            .GroupBy(static item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedMethods = typed.Methods
            .Where(method => duplicateNames.Contains(method.SourceName))
            .Select(method => Path.GetFullPath(string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath) + "\0" + method.SourceName + "\0" + method.SourceLine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedMethods.Count == 0)
            return typed;

        var excludedNames = typed.Methods
            .Where(method => duplicateNames.Contains(method.SourceName))
            .Select(static method => method.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePaths,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            excludedMethods,
            typed.Methods.Any(static method => method.RequiresPowerShellStreams)
                ? PowerShellCompilationCapability.PowerShellStreams |
                  PowerShellCompilationCapability.LocalFunctionCalls |
                  PowerShellCompilationCapability.BoundParameters |
                  PowerShellCompilationCapability.PowerShellObjects
                : PowerShellCompilationCapability.None);
        var diagnostics = excludedNames.Select(name =>
        {
            var definition = definitions
                .Where(item => item.Function.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static item => item.Function.Extent.StartOffset)
                .First();
            return new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Function '{name}' has multiple retained definitions, so hybrid compilation keeps PowerShell's runtime replacement semantics.",
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
}
