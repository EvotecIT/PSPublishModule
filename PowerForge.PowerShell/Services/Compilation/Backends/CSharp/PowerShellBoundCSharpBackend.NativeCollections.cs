using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string? _nativeRegionPartialOutputSink;

    private string EmitNativeCollectedValue(PowerShellLoweredExpression value, string? sink)
    {
        var previous = _nativeRegionPartialOutputSink;
        try { _nativeRegionPartialOutputSink = sink; return EmitExpression(value); }
        finally { _nativeRegionPartialOutputSink = previous; }
    }

    private string EmitNativeCollection(PowerShellLoweredNativeCollectionExpression collection, bool conditionalBranch = false)
    {
        if (collection.SingleExpression)
        {
            var value = collection.Items[0].Value;
            if (conditionalBranch)
            {
                // A single captured expression has an additional authored if-value error boundary.
                // Direct pipeline statements and multi-statement branches already route their own errors.
                var expression = EmitNativeCollection(collection);
                return "new global::System.Func<object?[]>(() => { try { return " + expression +
                    "; } catch (global::System.Exception __pfConditionalError) when (" + StatementErrorContextType +
                    ".IsOperationFailure(__pfConditionalError)) { __statementErrors.AppendEscapingConditionalError(__pfConditionalError); throw; } })()";
            }
            if (!collection.Items[0].EmitsOutput && value.ClrType != typeof(void))
                return "new global::System.Func<object?[]>(() => { " + PowerShellCSharpSymbolRenderer.TypeName(value.ClrType) + " " + collection.Items[0].ValueTemporary + " = " + EmitNativeCollectedValue(value, null) +
                    "; return __nativeFunction.CollectValue(global::System.Management.Automation.Internal.AutomationNull.Value); })()";
            return value.ClrType == typeof(void)
                ? "new global::System.Func<object?[]>(() => { " + EmitExpression(value) +
                    "; return __nativeFunction.CollectValue(global::System.Management.Automation.Internal.AutomationNull.Value); })()"
                : "__nativeFunction.CollectValue((object?)" + EmitExpression(value) + ")";
        }
        var body = new StringBuilder(collection.CollapseResult ? "new global::System.Func<object?>(() => { " : "new global::System.Func<object?[]>(() => { ");
        var result = collection.ResultTemporary;
        body.Append("var ").Append(result).Append(" = new global::System.Collections.Generic.List<object?>(); ");
        foreach (var item in collection.Items)
        {
            if (item.IsPipelineStatement && item.Value is PowerShellLoweredNativeCommandExpression command)
            {
                // The native statement owns its error boundary and writes records directly.
                body.Append(EmitNativeCommandRecords(command, result + ".Add")).Append("; ");
                continue;
            }
            body.Append("try { __statementErrors.SetNativeSequencePoint(")
                .Append(QuotePortableSourcePath(collection.SourcePath)).Append(", ")
                .Append(item.Span.StartLine).Append(", ").Append(item.Span.StartColumn).Append(", ")
                .Append(item.Span.EndLine).Append(", ").Append(item.Span.EndColumn).Append(", ")
                .Append(PowerShellCSharpLiteral.QuoteString(item.SourceText)).Append("); ");
            // Evaluate the entire expression before emitting records. An authored comma array
            // produces no records when a later member fails during its construction.
            if (item.Value.ClrType == typeof(void))
                body.Append(EmitNativeCollectedValue(item.Value, result + ".Add")).Append("; ");
            else if (!item.EmitsOutput)
                body.Append(PowerShellCSharpSymbolRenderer.TypeName(item.Value.ClrType)).Append(' ').Append(item.ValueTemporary).Append(" = ").Append(EmitNativeCollectedValue(item.Value, null)).Append("; ");
            else
                body.Append(PowerShellCSharpSymbolRenderer.TypeName(item.Value.ClrType)).Append(' ')
                    .Append(item.ValueTemporary).Append(" = ").Append(EmitNativeCollectedValue(item.Value, result + ".Add")).Append("; ")
                    .Append("__nativeFunction.WriteOutput(").Append(item.ValueTemporary).Append(", ")
                    .Append(result).Append(".Add); ");
            if (item.SetSuccess) body.Append("__nativeFunction.SetExecutionStatus(true); ");
            body.Append("} catch (global::System.Exception ").Append(item.ExceptionTemporary).Append(") when (")
                .Append(StatementErrorContextType).Append(".IsOperationFailure(").Append(item.ExceptionTemporary).Append(")) { ")
                .Append("__statementErrors.Handle(").Append(item.ExceptionTemporary).Append(", ")
                .Append(QuotePortableSourcePath(collection.SourcePath)).Append(", ")
                .Append(item.Span.StartLine).Append(", ").Append(item.Span.StartColumn).Append(", ")
                .Append(item.Span.EndLine).Append(", ").Append(item.Span.EndColumn).Append(", ")
                .Append(PowerShellCSharpLiteral.QuoteString(item.SourceText)).Append("); ");
            body.Append("} ");
        }
        if (collection.CollapseResult)
        {
            body.Append("return ").Append(result).Append(".Count == 0 ? global::System.Management.Automation.Internal.AutomationNull.Value : ")
                .Append(result).Append(".Count == 1 ? ").Append(result).Append("[0] : ").Append(result).Append(".ToArray(); })()");
            return body.ToString();
        }
        body.Append("if (").Append(result).Append(".Count == 0) return ")
            .Append(collection.ShareEmptyResult ? "global::System.Array.Empty<object>()" : "new object?[0]").Append("; ");
        body.Append("return ").Append(result).Append(".ToArray(); })()");
        return body.ToString();
    }

    private string EmitNativeConditionalValue(PowerShellLoweredNativeConditionalValueExpression conditionalValue)
    {
        var returnType = conditionalValue.PreserveRecords ? "object?[]" : "object?";
        var body = new StringBuilder("new global::System.Func<" + returnType + ">(() => { ");
        foreach (var clause in conditionalValue.Clauses)
            body.Append("if (").Append(EmitExpression(clause.Condition)).Append(") return (").Append(returnType).Append(')')
                .Append(EmitConditionalBranchValue(clause.Value)).Append("; ");
        body.Append("return (").Append(returnType).Append(')').Append(EmitConditionalBranchValue(conditionalValue.Otherwise))
            .Append("; })()");
        return body.ToString();
    }

    private string EmitConditionalBranchValue(PowerShellLoweredExpression value)
        => value is PowerShellLoweredNativeCollectionExpression collection
            ? EmitNativeCollection(collection, conditionalBranch: true)
            : EmitExpression(value);
}
