using System.Reflection;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Distinguishes authored Type values from literal enum receivers in native call arguments.</summary>
internal static class PowerShellNativeTypeArgumentPolicy
{
    internal static bool ContainsTypeValue(Ast syntax)
    {
        // Only a whole literal enum value is exempt. Transformations such as
        // EnumValue.GetType() must retain the existing authored-Type guard.
        if (PowerShellSemanticBinder.UnwrapExpression(syntax, preservePipeline: true) is
            MemberExpressionAst { Expression: TypeExpressionAst receiver } access &&
            access is not InvokeMemberExpressionAst && IsLiteralEnumReceiver(receiver))
            return false;
        return syntax.Find(static node => node is TypeExpressionAst,
            searchNestedScriptBlocks: false) is not null;
    }

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
