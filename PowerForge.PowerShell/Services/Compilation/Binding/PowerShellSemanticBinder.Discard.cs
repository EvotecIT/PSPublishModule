using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private bool TryBindStatementDiscard(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out PowerShellBoundStatement? bound)
    {
        bound = null;
        if (statement is not PipelineAst { PipelineElements.Count: 1 } discardPipeline ||
            discardPipeline.PipelineElements[0] is not CommandExpressionAst { Expression: ConvertExpressionAst conversion } ||
            !PowerShellCompilationConversionPolicy.IsStatementDiscard(conversion))
            return false;

        var operand = BindExpression(
            document,
            conversion.Child,
            symbols,
            functions,
            diagnostics,
            targetFramework: targetFramework,
            capabilities: capabilities);
        if (operand is not null)
        {
            bound = new PowerShellBoundExpressionStatement(
                PowerShellSourceParser.GetSpan(document, discardPipeline.Extent),
                operand,
                emitsOutput: false);
        }
        return true;
    }
}
