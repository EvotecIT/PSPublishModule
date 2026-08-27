using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellObjectSemanticBinder
{
    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        ConvertExpressionAst conversion,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, conversion.Extent);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) || conversion.Child is not HashtableAst hashtable)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2901", "[pscustomobject] literals require a generated binary-module host.", span));
            return null;
        }
        var properties = new List<PowerShellBoundNoteProperty>();
        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (pair.Item1 is not StringConstantExpressionAst key || string.IsNullOrWhiteSpace(key.Value))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2902", "Typed [pscustomobject] literals require non-empty literal string property names.", PowerShellSourceParser.GetSpan(document, pair.Item1.Extent)));
                return null;
            }
            if (pair.Item2 is not PipelineAst { PipelineElements.Count: 1 } pipeline || pipeline.PipelineElements[0] is not CommandExpressionAst command)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2903", "Typed [pscustomobject] note-property values must be one scalar expression.", PowerShellSourceParser.GetSpan(document, pair.Item2.Extent)));
                return null;
            }
            var value = bindExpression(command.Expression, null);
            if (value is null || value.Type.ClrType == typeof(void)) return null;
            properties.Add(new PowerShellBoundNoteProperty(key.Value, value));
        }
        return new PowerShellBoundPowerShellObjectExpression(span, properties.ToArray());
    }
}
