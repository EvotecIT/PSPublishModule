using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies direct function-local enums and data classes in their native lexical frame.</summary>
internal static class PowerShellHostedLocalTypePolicy
{
    internal static TypeDefinitionAst[] FindDeclarations(FunctionDefinitionAst function)
        => function.Body.EndBlock?.Statements.OfType<TypeDefinitionAst>()
            .Where(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.IsSupportedFunctionType).ToArray()
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
            if (!global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.IsSupportedFunctionType(definition) ||
                definition.Parent is not NamedBlockAst { Parent: ScriptBlockAst { Parent: FunctionDefinitionAst owner } } block ||
                !ReferenceEquals(block, owner.Body.EndBlock) || owner.Body.BeginBlock is not null || owner.Body.ProcessBlock is not null ||
                owner.Body.DynamicParamBlock is not null || owner.Body.GetType().GetProperty("CleanBlock")?.GetValue(owner.Body) is not null ||
                owner.Parent is not NamedBlockAst parent || !ReferenceEquals(parent, document.SyntaxRoot.EndBlock) ||
                PowerShellNativeFunctionBindingPolicy.Select(owner, capabilities, framework) is null)
                return false;
            foreach (var reference in document.SyntaxRoot.FindAll(node =>
                         node is TypeExpressionAst expression && RefersTo(expression.TypeName, definition.Name) ||
                         node is TypeConstraintAst constraint && RefersTo(constraint.TypeName, definition.Name) ||
                         node is AttributeAst attribute && RefersTo(attribute.TypeName, definition.Name),
                         searchNestedScriptBlocks: true))
            {
                if (!ReferenceEquals(FindFunction(reference), owner) ||
                    (definition.IsEnum
                        ? reference is not TypeExpressionAst expression || !IsAsDestination(expression)
                        : !IsSupportedClassConsumer(reference))) return false;
            }
        }
        return true;
    }

    internal static bool IsQualifiedDestination(ParsedSourceDocument document, TypeExpressionAst expression,
        string? framework, PowerShellCompilationCapability capabilities)
        => FindFunction(expression) is { } owner &&
           FindDeclarations(owner).Count(definition => RefersTo(expression.TypeName, definition.Name) &&
               (definition.IsEnum ? IsAsDestination(expression) : IsSupportedClassConsumer(expression))) == 1 &&
           IsQualified(document, framework, capabilities);

    internal static bool IsQualifiedClassConversion(ParsedSourceDocument document, TypeConstraintAst constraint,
        string? framework, PowerShellCompilationCapability capabilities)
        => IsSupportedClassConsumer(constraint) && FindFunction(constraint) is { } owner &&
           FindDeclarations(owner).Count(definition => definition.IsClass && RefersTo(constraint.TypeName, definition.Name)) == 1 &&
           IsQualified(document, framework, capabilities);

    private static bool RefersTo(ITypeName name, string declaration)
        => name is ArrayTypeName array ? RefersTo(array.ElementType, declaration) :
           name is GenericTypeName generic ? generic.GenericArguments.Any(argument => RefersTo(argument, declaration)) :
           name.FullName.Equals(declaration, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedClassConsumer(Ast reference)
        // A separately invoked script block cannot use the declaring function's active type scope.
        => FindFunction(reference, allowNestedScriptBlocks: false) is not null && (reference switch
        {
            TypeConstraintAst { Parent: ConvertExpressionAst } constraint => IsLexicalClassName(constraint.TypeName),
            TypeExpressionAst expression => IsLexicalClassName(expression.TypeName),
            _ => false
        });

    private static bool IsLexicalClassName(ITypeName name)
        => string.IsNullOrEmpty(name.AssemblyName) &&
           (name is TypeName || name is ArrayTypeName { Rank: 1, ElementType: TypeName element } &&
            string.IsNullOrEmpty(element.AssemblyName));

    private static bool IsAsDestination(TypeExpressionAst expression)
        => expression.TypeName is TypeName && string.IsNullOrEmpty(expression.TypeName.AssemblyName) &&
           expression.Parent is BinaryExpressionAst { Operator: TokenKind.As } binary && ReferenceEquals(binary.Right, expression);

    private static FunctionDefinitionAst? FindFunction(Ast ast, bool allowNestedScriptBlocks = true)
    {
        for (var parent = ast.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst function) return function;
            if (!allowNestedScriptBlocks && parent is ScriptBlockAst { Parent: not FunctionDefinitionAst }) return null;
        }
        return null;
    }
}
