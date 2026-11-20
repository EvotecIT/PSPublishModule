using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Adapts script-module startup to a native callback in its importing scope.</summary>
internal static class PowerShellNativeModuleInitializerPolicy
{
    internal const string MethodName = "__PowerForgeModuleInitializer";
    internal static ParsedSourceDocument? TryCreate(ParsedSourceDocument document)
    {
        var root = document.SyntaxRoot;
        if (root.BeginBlock is not null || root.ProcessBlock is not null ||
            root.DynamicParamBlock is not null || PowerShellSourceParser.HasUsingStatements(root) ||
            root.ScriptRequirements is not null || root.GetType().GetProperty("CleanBlock")?.GetValue(root) is not null ||
            root.EndBlock is not { Unnamed: true, Statements.Count: > 0 } end ||
            end.Traps?.Count > 0 || root.Find(node => node is TypeDefinitionAst && !ReferenceEquals(node.Parent, end),
                searchNestedScriptBlocks: false) is not null)
            return null;

        // The retained module owns the load guard and the class scope for both alternatives.
        if (PowerShellCompilationSourcePathPlanner.HasModuleAlternatives(root))
            return null;

        // Native type declarations are module metadata. The executable AST keeps the original
        // extents so resolved command and declaration regions retain the authored positions.
        var startup = end.Statements.Where(static statement => statement is not TypeDefinitionAst)
            .Select(static statement => (StatementAst)statement.Copy()).ToArray();
        if (startup.Length == 0) return null;
        var body = new ScriptBlockAst(root.Extent, null, new StatementBlockAst(end.Extent, startup, null), false);
        var function = new FunctionDefinitionAst(root.Extent, false, false, MethodName, null, body);
        var metadata = end.Statements.OfType<TypeDefinitionAst>().Select(static declaration => (StatementAst)declaration.Copy());
        var block = new StatementBlockAst(root.Extent, metadata.Append(function), null);
        return new ParsedSourceDocument(document.DocumentId, document.Path, document.Text,
            new ScriptBlockAst(root.Extent, null, block, false), document.Tokens, document.Errors,
            nativeScriptRootName: MethodName);
    }
}
