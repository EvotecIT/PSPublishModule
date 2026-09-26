using System.Reflection;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Distinguishes authored Type values from constructor and literal enum receivers in native call arguments.</summary>
internal static class PowerShellNativeTypeArgumentPolicy
{
    internal static bool ContainsTypeValue(Ast syntax)
    {
        // The native interpolation owner produces a String value. Its child
        // expressions still undergo their own binding and invocation guards;
        // a Type mentioned inside it is not passed as the enclosing value.
        if (PowerShellSemanticBinder.UnwrapExpression(syntax, preservePipeline: true) is ExpandableStringExpressionAst)
            return false;
        // A resolved constructor receiver denotes the instance being created;
        // it does not pass a System.Type value to the enclosing invocation.
        // Type-valued constructor arguments retain their existing guard.
        if (PowerShellSemanticBinder.UnwrapExpression(syntax, preservePipeline: true) is
            InvokeMemberExpressionAst { Static: true, Expression: TypeExpressionAst constructedType,
                Member: StringConstantExpressionAst constructor } creation &&
            constructor.Value.Equals("new", StringComparison.OrdinalIgnoreCase) &&
            constructedType.TypeName.GetReflectionType() is not null)
            return creation.Arguments?.Any(ContainsTypeValue) == true;
        // Only a whole literal enum value is exempt. Transformations such as
        // EnumValue.GetType() must retain the existing authored-Type guard.
        if (PowerShellSemanticBinder.UnwrapExpression(syntax, preservePipeline: true) is
            MemberExpressionAst { Expression: TypeExpressionAst receiver } access &&
            access is not InvokeMemberExpressionAst && IsLiteralEnumReceiver(receiver))
            return false;
        return syntax.Find(node => node is TypeExpressionAst type && !IsClosedValueReceiver(type, syntax),
            searchNestedScriptBlocks: false) is not null;
    }

    private static bool IsClosedValueReceiver(TypeExpressionAst syntax, Ast valueBoundary)
    {
        if (syntax.Parent is not InvokeMemberExpressionAst { Static: true,
                Member: StringConstantExpressionAst member } call ||
            !ReferenceEquals(call.Expression, syntax))
            return false;
        var type = syntax.TypeName.GetReflectionType();
        if (type is null) return false;
        // A resolved static scalar-returning call cannot pass its receiver Type
        // as its result. Keep object/Type/generic returns and unresolved members
        // conservative; this is not general return-value or alias analysis.
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(method => method.Name.Equals(member.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (methods.Length == 0 || methods.Any(static method =>
                method.ContainsGenericParameters || !IsClosedNonTypeResult(method.ReturnType)))
            return false;
        var resultTypes = methods.Select(static method => method.ReturnType).Distinct().ToArray();
        for (Ast value = call; ;)
        {
            // Success requires a proof up to the exact analyzed value. A
            // collection/index/subexpression or other unknown wrapper can
            // transform that value and must not silently terminate the proof.
            if (ReferenceEquals(value, valueBoundary)) return true;
            if (value.Parent is not { } parent) return false;
            // Parentheses preserve a value but insert pipeline wrappers. Follow
            // them before deciding whether a later operation still returns a value.
            if (parent is CommandExpressionAst { Redirections.Count: 0 } command && ReferenceEquals(command.Expression, value) ||
                parent is PipelineAst { PipelineElements.Count: 1 } pipeline && ReferenceEquals(pipeline.PipelineElements[0], value) ||
                parent is ParenExpressionAst paren && ReferenceEquals(paren.Pipeline, value))
            {
                value = parent;
                continue;
            }
            // Casts can produce Type values and can invoke authored conversions.
            // They require their own argument contract even after a scalar call.
            if (parent is ConvertExpressionAst conversion && ReferenceEquals(conversion.Child, value))
                return false;
            // The observed report marker combines one closed String value
            // with literal strings. Preserve only this inert concatenation;
            // arbitrary operands and overloads keep the Type-value guard.
            if (parent is BinaryExpressionAst { Operator: TokenKind.Plus } binary &&
                resultTypes.All(static result => result == typeof(string)) &&
                (ReferenceEquals(binary.Left, value) && binary.Right is StringConstantExpressionAst ||
                 ReferenceEquals(binary.Right, value) && binary.Left is StringConstantExpressionAst))
            {
                value = binary;
                continue;
            }
            if (parent is not MemberExpressionAst access || !ReferenceEquals(access.Expression, value))
                return false;
            if (access is not InvokeMemberExpressionAst { Static: false, Member: StringConstantExpressionAst nextMember })
                return false;
            var nextMethods = resultTypes.SelectMany(resultType => resultType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name.Equals(nextMember.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (nextMethods.Length == 0 || nextMethods.Any(static method =>
                    method.ContainsGenericParameters || !IsClosedNonTypeResult(method.ReturnType)))
                return false;
            resultTypes = nextMethods.Select(static method => method.ReturnType).Distinct().ToArray();
            value = access;
        }
    }

    private static bool IsClosedNonTypeResult(Type type)
        => PowerShellStableScalarTypePolicy.IsSupported(type) ||
           type.IsValueType && !type.ContainsGenericParameters;

    private static bool IsLiteralEnumReceiver(TypeExpressionAst syntax)
    {
        if (syntax.Parent is not MemberExpressionAst { Static: true, Member: StringConstantExpressionAst member } access ||
            access is InvokeMemberExpressionAst || !ReferenceEquals(access.Expression, syntax))
            return false;
        var type = syntax.TypeName.GetReflectionType();
        if (type is not { IsEnum: true }) return false;
        return type.GetFields(BindingFlags.Public | BindingFlags.Static).Any(field =>
            field.IsLiteral && field.FieldType == type &&
            field.Name.Equals(member.Value, StringComparison.OrdinalIgnoreCase));
    }
}
