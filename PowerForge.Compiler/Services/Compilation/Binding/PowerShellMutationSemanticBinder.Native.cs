using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellMutationSemanticBinder
{
    private static PowerShellBoundMutationExpression? BindNativeAssignment(ParsedSourceDocument document,
        AssignmentStatementAst syntax, VariableExpressionAst variable, PowerShellSemanticSymbolBinding target,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        var operation = GetAssignmentOperator(syntax.Operator);
        if (operation is null) return null;
        var value = bindExpression(syntax.Right, null);
        if (value is null || value.Type.ClrType == typeof(void)) return null;
        return new PowerShellBoundMutationExpression(span, target.Symbol, typeof(object), operation.Value, value,
            PowerShellTypeFact.Unknown, false, PowerShellIntegralMutationSemantics.None,
            nativeTargetRead: PowerShellNativeFunctionBindingPolicy.BindVariable(document, variable),
            nativeSourceText: PowerShellNativeFunctionBindingPolicy.SourceLines(document, span),
            nativeAssignmentTarget: new PowerShellNativeAssignmentTarget(
                syntax.Left.Extent.Text, document.Path, document.Text, PowerShellSourceParser.GetSpan(document, syntax.Left.Extent),
                syntax.Left.Extent.StartOffset, syntax.Left.Extent.EndOffset));
    }

    internal static PowerShellBoundMutationOperator? GetAssignmentOperator(TokenKind operation)
        => operation switch
        {
            TokenKind.Equals => PowerShellBoundMutationOperator.Assign,
            TokenKind.PlusEquals => PowerShellBoundMutationOperator.Add,
            TokenKind.MinusEquals => PowerShellBoundMutationOperator.Subtract,
            TokenKind.MultiplyEquals => PowerShellBoundMutationOperator.Multiply,
            TokenKind.DivideEquals => PowerShellBoundMutationOperator.Divide,
            TokenKind.RemainderEquals => PowerShellBoundMutationOperator.Remainder,
            _ => (PowerShellBoundMutationOperator?)null
        };
}
