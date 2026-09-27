using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies direct function-local enums without changing their lexical owner or admitting arbitrary unresolved types.</summary>
internal static class PowerShellHostedLocalEnumPolicy
{
    internal static TypeDefinitionAst[] FindDeclarations(FunctionDefinitionAst function)
        => function.Body.EndBlock?.Statements.OfType<TypeDefinitionAst>()
            .Where(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.IsSupportedFunctionEnum).ToArray()
           ?? Array.Empty<TypeDefinitionAst>();

    internal static bool IsQualified(ParsedSourceDocument document, string? framework, PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) || document.Errors.Length != 0) return false;
        // File-wide closure intentionally excludes nested function bodies. Preserve that contract;
        // inspect local declarations and consumers through their actual lexical AST owners instead.
        var definitions = document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst, searchNestedScriptBlocks: true)
            .OfType<TypeDefinitionAst>().ToArray();
        if (definitions.Length == 0 ||
            definitions.GroupBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() != 1))
            return false;
        foreach (var definition in definitions)
        {
            if (!global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.IsSupportedFunctionEnum(definition) ||
                definition.Parent is not NamedBlockAst { Parent: ScriptBlockAst { Parent: FunctionDefinitionAst owner } } block ||
                !ReferenceEquals(block, owner.Body.EndBlock) || owner.Body.BeginBlock is not null || owner.Body.ProcessBlock is not null ||
                owner.Body.DynamicParamBlock is not null || owner.Body.GetType().GetProperty("CleanBlock")?.GetValue(owner.Body) is not null ||
                owner.Parent is not NamedBlockAst parent || !ReferenceEquals(parent, document.SyntaxRoot.EndBlock) ||
                PowerShellNativeFunctionBindingPolicy.Select(owner, capabilities, framework) is null)
                return false;
            foreach (var reference in document.SyntaxRoot.FindAll(node =>
                         node is TypeExpressionAst expression && expression.TypeName.FullName.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) ||
                         node is TypeConstraintAst constraint && constraint.TypeName.FullName.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) ||
                         node is AttributeAst attribute && attribute.TypeName.FullName.Equals(definition.Name, StringComparison.OrdinalIgnoreCase),
                         searchNestedScriptBlocks: true))
            {
                if (reference is not TypeExpressionAst expression || !IsAsDestination(expression) ||
                    !ReferenceEquals(FindFunction(expression), owner)) return false;
            }
        }
        return true;
    }

    internal static bool IsQualifiedDestination(ParsedSourceDocument document, TypeExpressionAst expression,
        string? framework, PowerShellCompilationCapability capabilities)
        => IsAsDestination(expression) && FindFunction(expression) is { } owner &&
           FindDeclarations(owner).Count(definition => definition.Name.Equals(expression.TypeName.FullName, StringComparison.OrdinalIgnoreCase)) == 1 &&
           IsQualified(document, framework, capabilities);

    private static bool IsAsDestination(TypeExpressionAst expression)
        => expression.TypeName is TypeName && string.IsNullOrEmpty(expression.TypeName.AssemblyName) &&
           expression.Parent is BinaryExpressionAst { Operator: TokenKind.As } binary && ReferenceEquals(binary.Right, expression);

    private static FunctionDefinitionAst? FindFunction(Ast ast)
    {
        for (var parent = ast.Parent; parent is not null; parent = parent.Parent)
            if (parent is FunctionDefinitionAst function) return function;
        return null;
    }
}
