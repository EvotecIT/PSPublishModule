using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies document-owned data classes whose declarations and implicit construction stay native.</summary>
internal static class PowerShellHostedValueClassPolicy
{
    internal static bool RequiresNativeConstruction(FunctionDefinitionAst function)
    {
        Ast root = function;
        while (root.Parent is not null) root = root.Parent;
        var names = root.FindAll(static node => node is TypeDefinitionAst { IsClass: true }, searchNestedScriptBlocks: false)
            .OfType<TypeDefinitionAst>().Where(definition => root is ScriptBlockAst script && ReferenceEquals(definition.Parent, script.EndBlock))
            .Select(static definition => definition.Name).ToArray();
        return function.Body.Find(node => node is TypeExpressionAst expression && IsConstructorReceiver(expression) &&
            names.Count(name => name.Equals(expression.TypeName.FullName, StringComparison.OrdinalIgnoreCase)) == 1,
            searchNestedScriptBlocks: true) is not null;
    }

    internal static bool IsQualified(ParsedSourceDocument document, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) || document.Errors.Length != 0 ||
            document.TypeClosure.Declarations.Length == 0 ||
            document.TypeClosure.Declarations.Any(static declaration => declaration.IsEnum || !declaration.IsDocumentScope)) return false;
        var definitions = document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst,
            searchNestedScriptBlocks: false).OfType<TypeDefinitionAst>().ToArray();
        if (definitions.Length != document.TypeClosure.Declarations.Length ||
            definitions.GroupBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() != 1) ||
            definitions.Any(definition => !QualifiesDefinition(definition, targetFramework))) return false;
        foreach (var reference in document.TypeClosure.References.Where(static item => item.DeclarationCandidates.Length != 0))
        {
            if (reference.Kind != PowerShellSourceTypeReferenceKind.Value || reference.DeclarationCandidates.Length != 1) return false;
            var expression = document.SyntaxRoot.FindAll(static node => node is TypeExpressionAst, searchNestedScriptBlocks: true)
                .OfType<TypeExpressionAst>().SingleOrDefault(candidate => PowerShellSourceParser.GetSpan(document, candidate.TypeName.Extent).Equals(reference.Span));
            if (expression is null || !IsConstructorReceiver(expression)) return false;
            for (Ast? parent = expression.Parent; parent is not null; parent = parent.Parent)
                if (parent is FunctionDefinitionAst function &&
                    PowerShellNativeFunctionBindingPolicy.Select(function, capabilities, targetFramework) is not { HasClean: false }) return false;
        }
        return true;
    }

    internal static bool IsQualifiedConstructorReceiver(ParsedSourceDocument document, TypeExpressionAst expression,
        string? targetFramework, PowerShellCompilationCapability capabilities)
        => IsConstructorReceiver(expression) && IsQualified(document, targetFramework, capabilities) &&
           expression.TypeName is TypeName && string.IsNullOrEmpty(expression.TypeName.AssemblyName) &&
           document.TypeClosure.Declarations.Count(declaration => declaration.Name.Equals(expression.TypeName.FullName,
               StringComparison.OrdinalIgnoreCase)) == 1;

    private static bool IsConstructorReceiver(TypeExpressionAst expression)
        => expression.Parent is InvokeMemberExpressionAst { Static: true,
            Member: StringConstantExpressionAst name } invocation && ReferenceEquals(invocation.Expression, expression) &&
           (invocation.Arguments is null || invocation.Arguments.Count == 0) &&
           name.Value.Equals("new", StringComparison.OrdinalIgnoreCase);

    private static bool QualifiesDefinition(TypeDefinitionAst definition, string? targetFramework)
        => definition.IsClass && definition.Attributes.Count == 0 && definition.BaseTypes.Count == 0 &&
           definition.Members.All(member => member is PropertyMemberAst property && !property.IsStatic && property.IsPublic &&
               property.Attributes.Count == 0 && property.PropertyType?.TypeName.GetReflectionType() is { } type &&
               (type == typeof(string) || type == typeof(int) || type == typeof(long) || type == typeof(bool)) &&
               PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework) &&
               (property.InitialValue is null or ConstantExpressionAst ||
                property.InitialValue is MemberExpressionAst { Static: true, Expression: TypeExpressionAst receiver,
                    Member: StringConstantExpressionAst { Value: "MachineName" } } and not InvokeMemberExpressionAst &&
                receiver.TypeName.GetReflectionType() == typeof(Environment)));
}
