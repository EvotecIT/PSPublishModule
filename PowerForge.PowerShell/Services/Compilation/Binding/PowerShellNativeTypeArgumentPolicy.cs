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
        return syntax.Find(node => node is TypeExpressionAst type &&
            !IsClosedTypeTestOperand(type, syntax) && !IsClosedValueReceiver(type, syntax),
            searchNestedScriptBlocks: false) is not null;
    }

    // A type-test predicate yields Boolean, not its authored Type operand. Prove
    // that result up to the analyzed value or an if condition; transformations
    // such as GetType(), casts and collection access retain their own guards.
    private static bool IsClosedTypeTestOperand(TypeExpressionAst syntax, Ast valueBoundary)
    {
        if (syntax.Parent is not BinaryExpressionAst { Operator: TokenKind.Is or TokenKind.IsNot } test)
            return false;
        for (Ast value = test; ;)
        {
            if (ReferenceEquals(value, valueBoundary)) return true;
            if (value.Parent is not { } parent) return false;
            if (parent is CommandExpressionAst { Redirections.Count: 0 } command && ReferenceEquals(command.Expression, value) ||
                parent is PipelineAst { PipelineElements.Count: 1 } pipeline && ReferenceEquals(pipeline.PipelineElements[0], value) ||
                parent is ParenExpressionAst paren && ReferenceEquals(paren.Pipeline, value) ||
                parent is BinaryExpressionAst { Operator: TokenKind.And or TokenKind.Or or TokenKind.Xor } ||
                parent is UnaryExpressionAst { TokenKind: TokenKind.Not or TokenKind.Exclaim })
            {
                value = parent;
                continue;
            }
            return parent is IfStatementAst conditional &&
                conditional.Clauses.Any(clause => ReferenceEquals(clause.Item1, value));
        }
    }

    private static bool IsClosedValueReceiver(TypeExpressionAst syntax, Ast valueBoundary)
    {
        if (syntax.Parent is not MemberExpressionAst { Static: true,
                Member: StringConstantExpressionAst member } rootAccess || !ReferenceEquals(rootAccess.Expression, syntax))
            return false;
        var type = syntax.TypeName.GetReflectionType();
        if (type is null) return false;
        Type[] resultTypes;
        if (rootAccess is InvokeMemberExpressionAst)
        {
            // Preserve the existing scalar/vector method-result proof.
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(method => method.Name.Equals(member.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (methods.Length == 0 || methods.Any(static method =>
                    method.ContainsGenericParameters || !IsClosedNonTypeResult(method.ReturnType)))
                return false;
            resultTypes = methods.Select(static method => method.ReturnType).Distinct().ToArray();
        }
        else
        {
            if (!TryGetClosedStaticValueTypes(type, member.Value, out resultTypes)) return false;
        }
        for (Ast value = rootAccess; ;)
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

    /// <summary>Proves a resolved static field/property value without evaluating its getter.</summary>
    internal static bool RequiresNativeStaticValueRead(MemberExpressionAst access)
        => access is not InvokeMemberExpressionAst && access.Static &&
           access.Expression is TypeExpressionAst receiver && receiver.TypeName.GetReflectionType() is { } type &&
           access.Member is StringConstantExpressionAst member &&
           TryGetClosedStaticValueTypes(type, member.Value, out var results) &&
           results.All(IsClosedNonTypeReferenceResult);

    private static bool TryGetClosedStaticValueTypes(Type type, string name, out Type[] results)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var fields = type.GetFields(flags).Where(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var properties = type.GetProperties(flags).Where(property =>
            property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            property.GetMethod is { IsPublic: true, IsStatic: true } && property.GetIndexParameters().Length == 0).ToArray();
        results = fields.Select(static field => field.FieldType)
            .Concat(properties.Select(static property => property.PropertyType)).Distinct().ToArray();
        // Inert scalar fields retain CLR facts. New class-valued reads use the SDK owner.
        // Scalar getters need separate failure/evaluation proof and retain the existing guard.
        return results.Length > 0 && fields.All(static field =>
                IsClosedNonTypeResult(field.FieldType) || IsClosedNonTypeReferenceResult(field.FieldType)) &&
            properties.All(static property => IsClosedNonTypeReferenceResult(property.PropertyType));
    }

    private static bool IsClosedNonTypeReferenceResult(Type result)
        // A non-generic, non-enumerable class unrelated to Type cannot return a Type instance.
        // Object, Type ancestry, interfaces, wrappers, and unknown collection shapes stay conservative.
        => result.IsClass && !result.IsArray && !typeof(System.Collections.IEnumerable).IsAssignableFrom(result) &&
           !result.IsGenericType && !result.ContainsGenericParameters &&
           !result.IsAssignableFrom(typeof(Type)) && !typeof(Type).IsAssignableFrom(result) &&
           !typeof(System.Management.Automation.PSObject).IsAssignableFrom(result);

    private static bool IsClosedNonTypeResult(Type type)
        => PowerShellStableScalarTypePolicy.IsSupported(type) ||
           type.IsValueType && !type.ContainsGenericParameters ||
           type.IsArray && type.GetElementType() is { } elementType && IsClosedNonTypeResult(elementType);

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
