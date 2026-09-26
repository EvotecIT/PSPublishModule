using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // Direct static '=' assignment keeps its existing CLR owner. This route owns
    // nested accesses whose runtime receiver/conversion is observed by PowerShell.
    internal static TypeExpressionAst? NativeStaticAssignmentReceiver(ExpressionAst target)
        => target is MemberExpressionAst { Static: false } or IndexExpressionAst
            ? Generated.Runtime.PowerShellNativeFunctionContext.FindNativeStaticAccessReceiver(target) : null;

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
