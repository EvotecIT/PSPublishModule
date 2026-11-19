using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // Mutable static property values may require PowerShell conversion and adapter
    // dispatch. A literal scriptblock can still use the typed delegate conversion path.
    internal static TypeExpressionAst? NativeStaticAssignmentReceiver(ExpressionAst target, string? targetFramework,
        PowerShellNativeDependencyTypes? dependencyTypes = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None,
        StatementAst? value = null)
    {
        var staticRoot = Generated.Runtime.PowerShellNativeFunctionContext.FindNativeStaticAccessReceiver(target);
        if (staticRoot is not null && (dependencyTypes?.Qualifies(staticRoot.TypeName, capabilities) == true ||
            PowerShellHostedGuardedAddTypePolicy.IsQualified(staticRoot, capabilities)))
            return staticRoot;
        if (target is MemberExpressionAst { Static: true, Expression: TypeExpressionAst type,
                Member: StringConstantExpressionAst name } && type.TypeName.GetReflectionType() is { } receiver)
        {
            var members = receiver.GetMember(name.Value, MemberTypes.Field | MemberTypes.Property,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase);
            // Bind edition-dependent member absence in the active host so skipped branches stay skipped
            // and reached branches report the native assignment error at the authored statement.
            if (members.Length == 0 && staticRoot is not null &&
                PowerShellCompilationParameterTypePolicy.CanUseInMethod(receiver, targetFramework, capabilities))
                return staticRoot;
            if (members.Length == 1 && members[0] is PropertyInfo property &&
                (property.PropertyType.IsEnum || property.PropertyType.IsClass &&
                    property.PropertyType != typeof(string) &&
                    (!typeof(MulticastDelegate).IsAssignableFrom(property.PropertyType) ||
                     value is not null && UnwrapExpression(value) is not ScriptBlockExpressionAst)) &&
                property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } &&
                property.GetIndexParameters().Length == 0 &&
                (string.IsNullOrWhiteSpace(targetFramework) ||
                 PowerShellGeneratedMemberPolicy.IsWritableSupported(property, targetFramework!))) return type;
        }
        return target is MemberExpressionAst { Static: false } or IndexExpressionAst
            ? staticRoot : null;
    }

    private PowerShellBoundStatement? BindNativeStaticAssignment(ParsedSourceDocument document,
        AssignmentStatementAst assignment, TypeExpressionAst receiver,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var span = PowerShellSourceParser.GetSpan(document, assignment.Extent);
        var dependencyReceiver = document.NativeDependencyTypes.Qualifies(receiver.TypeName, capabilities) ||
            PowerShellHostedGuardedAddTypePolicy.IsQualified(receiver, capabilities);
        var type = dependencyReceiver ? null : receiver.TypeName.GetReflectionType();
        if (!dependencyReceiver &&
            (type is null || !PowerShellCompilationParameterTypePolicy.CanUseInMethod(type, targetFramework, capabilities)))
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
        var typeName = type?.FullName ?? receiver.TypeName.FullName;
        return new PowerShellBoundNativeAssignmentStatement(span, typeName, value, operation.Value,
            new PowerShellNativeAssignmentTarget(assignment.Left.Extent.Text, document.Path, document.Text,
                PowerShellSourceParser.GetSpan(document, assignment.Left.Extent), assignment.Left.Extent.StartOffset,
                assignment.Left.Extent.EndOffset, MutatesReceiver: true,
                ReadVariables: assignment.Left.FindAll(static node => node is VariableExpressionAst, false)
                    .Cast<VariableExpressionAst>().Select(static variable => variable.VariablePath.UserPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StaticReceiverTypeName: typeName));
    }
}
