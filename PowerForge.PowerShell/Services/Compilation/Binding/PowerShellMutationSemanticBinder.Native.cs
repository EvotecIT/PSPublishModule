using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellMutationSemanticBinder
{
    private static PowerShellBoundMutationExpression? BindNativeAssignment(ParsedSourceDocument document,
        AssignmentStatementAst syntax, VariableExpressionAst variable, PowerShellSemanticSymbolBinding target,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Left is not VariableExpressionAst)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2417",
                "Adding a native variable constraint requires its native constraint-transition contract.", span));
            return null;
        }
        var operation = syntax.Operator switch
        {
            TokenKind.Equals => PowerShellBoundMutationOperator.Assign,
            TokenKind.PlusEquals => PowerShellBoundMutationOperator.Add,
            TokenKind.MinusEquals => PowerShellBoundMutationOperator.Subtract,
            TokenKind.MultiplyEquals => PowerShellBoundMutationOperator.Multiply,
            TokenKind.DivideEquals => PowerShellBoundMutationOperator.Divide,
            TokenKind.RemainderEquals => PowerShellBoundMutationOperator.Remainder,
            _ => (PowerShellBoundMutationOperator?)null
        };
        if (operation is null) return null;
        var value = bindExpression(syntax.Right, null);
        if (value is null || value.Type.ClrType == typeof(void)) return null;
        return new PowerShellBoundMutationExpression(span, target.Symbol, typeof(object), operation.Value, value,
            PowerShellTypeFact.Unknown, false, PowerShellIntegralMutationSemantics.None,
            nativeTargetRead: PowerShellNativeFunctionBindingPolicy.BindVariable(document, variable),
            nativeSourceText: PowerShellNativeFunctionBindingPolicy.SourceLines(document, span));
    }
}
