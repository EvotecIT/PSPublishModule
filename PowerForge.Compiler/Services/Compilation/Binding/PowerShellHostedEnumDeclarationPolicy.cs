using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies authored enums retained as native parameter metadata and literal member-value owners.</summary>
/// <remarks>Declaration identity comes from the pinned document, never an arbitrary unresolved type name.</remarks>
internal static class PowerShellHostedEnumDeclarationPolicy
{
    internal static bool RequiresNativeParameterBinding(FunctionDefinitionAst function)
    {
        if (function.Parent is not NamedBlockAst { Parent: ScriptBlockAst root }) return false;
        var names = root.FindAll(static node => node is TypeDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<TypeDefinitionAst>().Where(definition => definition.IsEnum && ReferenceEquals(definition.Parent, root.EndBlock))
            .GroupBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1).Select(static group => group.Key).ToArray();
        return PowerShellParameterSyntax.GetParameters(function.Body).SelectMany(static parameter => parameter.Attributes)
            .OfType<TypeConstraintAst>().Any(constraint => NamesEnum(constraint.TypeName, names)) ||
            function.Body.Find(node => node is TypeExpressionAst expression && IsMemberReceiver(expression) &&
                NamesEnum(expression.TypeName, names), searchNestedScriptBlocks: false) is not null;
    }

    internal static bool IsQualified(ParsedSourceDocument document, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) || document.Errors.Length != 0 ||
            document.TypeClosure.Declarations.Length == 0 ||
            document.TypeClosure.Declarations.Any(static declaration => !declaration.IsEnum || !declaration.IsDocumentScope))
            return false;
        var definitions = document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst,
            searchNestedScriptBlocks: false).OfType<TypeDefinitionAst>().ToArray();
        if (definitions.Length != document.TypeClosure.Declarations.Length ||
            definitions.GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() != 1) ||
            definitions.Any(static definition => definition.Attributes.Any(attribute =>
                    attribute.TypeName.GetReflectionAttributeType() != typeof(FlagsAttribute) ||
                    attribute.PositionalArguments.Count != 0 || attribute.NamedArguments.Count != 0) || definition.BaseTypes.Count != 0 ||
                definition.Members.Any(static member => member is not PropertyMemberAst property ||
                    property.Attributes.Count != 0 || property.InitialValue is not null and not ConstantExpressionAst { Value: int })))
            return false;
        var functions = document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst,
            searchNestedScriptBlocks: false).OfType<FunctionDefinitionAst>()
            .Where(function => ReferenceEquals(function.Parent, document.SyntaxRoot.EndBlock)).ToArray();
        foreach (var reference in document.TypeClosure.References.Where(static item => item.DeclarationCandidates.Length != 0))
        {
            if (reference.DeclarationCandidates.Length != 1)
                return false;
            var function = functions.SingleOrDefault(candidate =>
                PowerShellSourceParser.GetSpan(document, candidate.Extent).Equals(reference.ConsumerSpan));
            if (function is null || PowerShellNativeFunctionBindingPolicy.Select(function, capabilities, targetFramework) is not { HasClean: false })
                return false;
            if (reference.Kind == PowerShellSourceTypeReferenceKind.Value)
            {
                var expression = function.Body.FindAll(static node => node is TypeExpressionAst, searchNestedScriptBlocks: false)
                    .OfType<TypeExpressionAst>().SingleOrDefault(candidate =>
                        PowerShellSourceParser.GetSpan(document, candidate.TypeName.Extent).Equals(reference.Span));
                if (expression is null || !IsDeclaredMemberReceiver(document, expression)) return false;
            }
            else if (reference.Kind != PowerShellSourceTypeReferenceKind.Constraint ||
                !PowerShellParameterSyntax.GetParameters(function.Body).SelectMany(static parameter => parameter.Attributes)
                    .OfType<TypeConstraintAst>().Any(constraint => IsEnumParameterType(document, constraint.TypeName) &&
                        ContainsTypeSpan(document, constraint.TypeName, reference.Span)))
                return false;
        }
        return true;
    }

    internal static bool IsQualifiedMemberReceiver(ParsedSourceDocument document, TypeExpressionAst expression,
        string? targetFramework, PowerShellCompilationCapability capabilities)
        => IsDeclaredMemberReceiver(document, expression) && IsQualified(document, targetFramework, capabilities);

    private static bool IsMemberReceiver(TypeExpressionAst expression)
        => expression.Parent is MemberExpressionAst { Static: true, Member: StringConstantExpressionAst } member &&
           member is not InvokeMemberExpressionAst && ReferenceEquals(member.Expression, expression);

    private static bool IsDeclaredMemberReceiver(ParsedSourceDocument document, TypeExpressionAst expression)
        => IsMemberReceiver(expression) && expression.TypeName is TypeName && string.IsNullOrEmpty(expression.TypeName.AssemblyName) &&
           document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst { IsEnum: true }, searchNestedScriptBlocks: false)
               .OfType<TypeDefinitionAst>().Count(definition => definition.Name.Equals(expression.TypeName.FullName, StringComparison.OrdinalIgnoreCase) &&
                   definition.Members.Any(member => member.Name.Equals(((StringConstantExpressionAst)((MemberExpressionAst)expression.Parent).Member).Value,
                       StringComparison.OrdinalIgnoreCase))) == 1;

    internal static bool IsQualifiedParameter(ParsedSourceDocument document, TypeConstraintAst constraint,
        string? targetFramework, PowerShellCompilationCapability capabilities)
        => IsQualified(document, targetFramework, capabilities) && IsEnumParameterType(document, constraint.TypeName);

    private static bool IsEnumParameterType(ParsedSourceDocument document, ITypeName name)
        => NamesEnum(name, document.TypeClosure.Declarations.Where(static declaration => declaration.IsEnum)
            .Select(static declaration => declaration.Name).ToArray());

    private static bool NamesEnum(ITypeName name, string[] names)
    {
        if (name is ArrayTypeName array)
            return array.Rank == 1 && array.ElementType is not ArrayTypeName && NamesEnum(array.ElementType, names);
        return name is TypeName && string.IsNullOrEmpty(name.AssemblyName) &&
            names.Count(candidate => candidate.Equals(name.FullName, StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private static bool ContainsTypeSpan(ParsedSourceDocument document, ITypeName name, SourceSpan span)
        => PowerShellSourceParser.GetSpan(document, name.Extent).Equals(span) ||
            name is ArrayTypeName array && ContainsTypeSpan(document, array.ElementType, span);
}
