using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // Enum-valued static properties require PowerShell's native conversion, including
    // flags produced by runtime-valued operators. Other direct static storage keeps
    // its existing CLR owner; nested access already uses native receiver semantics.
    internal static TypeExpressionAst? NativeStaticAssignmentReceiver(ExpressionAst target, string? targetFramework)
    {
        if (target is MemberExpressionAst { Static: true, Expression: TypeExpressionAst type,
                Member: StringConstantExpressionAst name } && type.TypeName.GetReflectionType() is { } receiver)
        {
            var members = receiver.GetMember(name.Value, MemberTypes.Field | MemberTypes.Property,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase);
            if (members.Length == 1 && members[0] is PropertyInfo property && property.PropertyType.IsEnum &&
                property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } &&
                property.GetIndexParameters().Length == 0 &&
                (string.IsNullOrWhiteSpace(targetFramework) ||
                 PowerShellGeneratedMemberPolicy.IsWritableSupported(property, targetFramework!))) return type;
        }
        return target is MemberExpressionAst { Static: false } or IndexExpressionAst
            ? Generated.Runtime.PowerShellNativeFunctionContext.FindNativeStaticAccessReceiver(target) : null;
    }

    private PowerShellBoundStatement? BindNativeStaticAssignment(ParsedSourceDocument document,
        AssignmentStatementAst assignment, TypeExpressionAst receiver,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var span = PowerShellSourceParser.GetSpan(document, assignment.Extent);
        var type = receiver.TypeName.GetReflectionType();
        if (type is null || !PowerShellCompilationParameterTypePolicy.CanUseInMethod(type, targetFramework, capabilities))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2611",
                $"CLR type '{receiver.TypeName.FullName}' is not available in the generated project reference set for the requested target.", span));
            return null;
        }
        var operation = PowerShellMutationSemanticBinder.GetAssignmentOperator(assignment.Operator);
        if (operation is null) return null;
        var value = BindExpression(document, assignment.Right, symbols, functions, diagnostics,
            targetFramework: targetFramework, capabilities: capabilities);
        if (value is null || value.Type.ClrType == typeof(void)) return null;
        return new PowerShellBoundNativeAssignmentStatement(span, type.FullName!, value, operation.Value,
            new PowerShellNativeAssignmentTarget(assignment.Left.Extent.Text, document.Path, document.Text,
                PowerShellSourceParser.GetSpan(document, assignment.Left.Extent), assignment.Left.Extent.StartOffset,
                assignment.Left.Extent.EndOffset, MutatesReceiver: true,
                ReadVariables: assignment.Left.FindAll(static node => node is VariableExpressionAst, false)
                    .Cast<VariableExpressionAst>().Select(static variable => variable.VariablePath.UserPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StaticReceiverTypeName: type.FullName));
    }
}
