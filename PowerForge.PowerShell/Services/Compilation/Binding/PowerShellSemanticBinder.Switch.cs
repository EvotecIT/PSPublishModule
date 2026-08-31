using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundStatement? BindSwitchStatement(
        ParsedSourceDocument document,
        SwitchStatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if ((statement.Flags & (SwitchFlags.File | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                $"Switch flags '{statement.Flags}' require PowerShell runtime matching semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }

        var matchMode = (statement.Flags & SwitchFlags.Regex) != 0
            ? PowerShellBoundSwitchMatchMode.Regex
            : PowerShellBoundSwitchMatchMode.Exact;
        if (PowerShellAutomaticVariableObservationPolicy.Observes(statement, "_", "PSItem", "switch"))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                "Scalar switch whose $_, $PSItem, or $switch automatic-variable state is observed requires PowerShell runtime semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }
        if (matchMode == PowerShellBoundSwitchMatchMode.Regex &&
            PowerShellAutomaticVariableObservationPolicy.Observes(statement, "Matches"))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                "Regex switch whose $Matches automatic-variable state is observed requires PowerShell runtime semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }

        var value = BindExpression(
            document,
            statement.Condition,
            symbols,
            functions,
            diagnostics,
            targetFramework: targetFramework,
            capabilities: capabilities);
        if (value is null) return null;
        var valueType = value.Type.ClrType;
        if (matchMode == PowerShellBoundSwitchMatchMode.Regex && valueType != typeof(string))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2305",
                $"Scalar regex switch requires a String condition; resolved type was '{valueType.FullName}'.",
                value.Span));
            return null;
        }
        if (valueType != typeof(bool) &&
            valueType != typeof(char) &&
            valueType != typeof(string) &&
            !PowerShellClrTypeSemantics.IsNumeric(valueType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2305",
                $"Scalar switch requires a Boolean, character, string, or numeric condition; resolved type was '{valueType.FullName}'.",
                value.Span));
            return null;
        }

        var clauses = new List<PowerShellBoundSwitchClause>();
        foreach (var clause in statement.Clauses)
        {
            var clauseValue = BindExpression(
                document,
                clause.Item1,
                symbols,
                functions,
                diagnostics,
                valueType,
                targetFramework,
                capabilities);
            if (clauseValue is null) return null;
            if (clauseValue.Type.ClrType != valueType)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2306",
                    $"Scalar switch clause type '{clauseValue.Type.ClrType.FullName}' must exactly match condition type '{valueType.FullName}' to avoid PowerShell coercion semantics.",
                    clauseValue.Span));
                return null;
            }
            if (matchMode == PowerShellBoundSwitchMatchMode.Regex &&
                !PowerShellSwitchRegexPatternPolicy.TryValidateLiteral(clauseValue, out var patternDiagnostic))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2306", patternDiagnostic, clauseValue.Span));
                return null;
            }

            var body = BindBlock(document, clause.Item2, symbols, functions, diagnostics, targetFramework, capabilities);
            if (body is null) return null;
            clauses.Add(new PowerShellBoundSwitchClause(clauseValue, body));
        }

        var defaultBlock = statement.Default is null
            ? null
            : BindBlock(document, statement.Default, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement.Default is not null && defaultBlock is null) return null;
        return new PowerShellBoundSwitchStatement(
            PowerShellSourceParser.GetSpan(document, statement.Extent),
            value,
            clauses.ToArray(),
            defaultBlock,
            matchMode,
            (statement.Flags & SwitchFlags.CaseSensitive) != 0);
    }
}
