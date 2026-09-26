using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitOutputCapture(StringBuilder builder, PowerShellLoweredOutputCaptureStatement capture,
        int indent, Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        if (capture.CapturesStableScalarVector)
        {
            EmitStableScalarVectorCapture(builder, capture, indent, getTemporaryIdentifier, discardHelper, sourceMap);
            return;
        }
        if (capture.NativeTarget is not null)
        {
            // The native owner reads a compound destination before evaluating the
            // compiled loop. Emitting directly into this builder preserves source maps.
            builder.Append(prefix).Append(EmitNativeAssignmentStart(capture.NativeTarget, capture.Operation)).AppendLine("() =>");
            builder.Append(prefix).AppendLine("{");
            indent++;
            prefix = new string(' ', indent * 4);
        }
        var records = capture.RecordsTemporary;
        var previous = capture.SinkTemporary;
        builder.Append(prefix).Append("var ").Append(records).AppendLine(" = new global::System.Collections.Generic.List<object?>();");
        builder.Append(prefix).Append("var ").Append(previous).AppendLine(" = __writeOutput;");
        var record = getTemporaryIdentifier("capturedRecord");
        builder.Append(prefix).Append("__writeOutput = ").Append(record)
            .Append(" => { if (!global::System.Object.ReferenceEquals(").Append(record)
            .Append(", global::System.Management.Automation.Internal.AutomationNull.Value)) ")
            .Append(records).Append(".Add(").Append(record).AppendLine("); };");
        builder.Append(prefix).AppendLine("try");
        builder.Append(prefix).AppendLine("{");
        if (capture.UsesNativeInvocation)
        {
            builder.Append(prefix).AppendLine("    using (__nativeFunction.RedirectOutput(__writeOutput))");
            builder.Append(prefix).AppendLine("    {");
        }
        _outputCaptureDepth++;
        try
        {
            foreach (var statement in capture.Statements)
                EmitStatement(builder, statement, indent + (capture.UsesNativeInvocation ? 2 : 1), getTemporaryIdentifier, discardHelper, sourceMap);
        }
        finally { _outputCaptureDepth--; }
        if (capture.UsesNativeInvocation)
            builder.Append(prefix).AppendLine("    }");
        builder.Append(prefix).Append("    ");
        var value = capture.UsesNativeInvocation ? getTemporaryIdentifier("collapsedCapture") : null;
        if (value is not null)
            builder.Append("object? ").Append(value).Append(" = ");
        else
            builder.Append(RenderStorage(capture.Target!)).Append(" = ");
        if (capture.Kind == PowerShellOutputCaptureKind.NativeObjectArray)
            builder.Append(records).Append(".Count == 0 ? ")
                .Append(capture.ShareEmptyArray ? "global::System.Array.Empty<object>()" : "new object?[0]")
                .Append(" : ").Append(records).AppendLine(".ToArray();");
        else
            builder.Append(records).Append(".Count == 0 ? global::System.Management.Automation.Internal.AutomationNull.Value : ").Append(records).Append(".Count == 1 ? ")
                .Append(records).Append("[0] : ").Append(records).Append(".ToArray()")
                .AppendLine(";");
        builder.Append(prefix).Append("    ").Append(records).AppendLine(".Clear();");
        if (value is not null)
            builder.Append(prefix).Append("    return ").Append(value).AppendLine(";");
        builder.Append(prefix).AppendLine("}");
        // PowerShell discards assignment output for RuntimeException, but flushes
        // pending records when a raw CLR exception escapes (for example MoveNext).
        builder.Append(prefix).Append("catch (global::System.Management.Automation.RuntimeException) { ")
            .Append(records).AppendLine(".Clear(); throw; }");
        if (capture.Kind == PowerShellOutputCaptureKind.NativeObjectArray)
            builder.Append(prefix).Append("catch (global::PowerForge.Generated.Runtime.PowerShellCapturedReturnSignal) { ")
                .Append(records).AppendLine(".Clear(); throw; }");
        var flushedRecord = getTemporaryIdentifier("flushedCaptureRecord");
        builder.Append(prefix).AppendLine("finally");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    __writeOutput = ").Append(previous).AppendLine(";");
        builder.Append(prefix).Append("    foreach (var ").Append(flushedRecord).Append(" in ").Append(records)
            .Append(") ").Append(previous).Append('(').Append(flushedRecord).AppendLine(");");
        builder.Append(prefix).AppendLine("}");
        if (capture.NativeTarget is not null)
            builder.Append(new string(' ', (indent - 1) * 4)).Append('}')
                .Append(EmitNativeAssignmentLocation(capture.NativeTarget)).AppendLine(";");
    }

    private void EmitStableScalarVectorCapture(
        StringBuilder builder,
        PowerShellLoweredOutputCaptureStatement capture,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var target = capture.Target ?? throw new InvalidOperationException("A stable vector capture requires a local target.");
        var elementType = capture.CapturedElementType ??
            throw new InvalidOperationException("A stable vector capture requires an element type.");
        var prefix = new string(' ', indent * 4);
        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(elementType);
        var targetTypeName = elementTypeName + "[]";
        if (capture.DeclareTarget)
            builder.Append(prefix).Append(targetTypeName).Append(' ').Append(RenderStorage(target)).AppendLine(" = default!;");
        builder.Append(prefix).Append("var ").Append(capture.RecordsTemporary)
            .Append(" = new global::System.Collections.Generic.List<").Append(elementTypeName).AppendLine(">();");
        var record = getTemporaryIdentifier("capturedVectorRecord");
        builder.Append(prefix).Append("global::System.Action<object?> ").Append(capture.SinkTemporary)
            .Append(" = ").Append(record).Append(" => { if (!global::System.Object.ReferenceEquals(")
            .Append(record).Append(", global::System.Management.Automation.Internal.AutomationNull.Value)) ")
            .Append(capture.RecordsTemporary).Append(".Add((").Append(elementTypeName).Append(')')
            .Append(record).AppendLine("!); };");
        foreach (var statement in capture.Statements)
            EmitStatement(builder, statement, indent, getTemporaryIdentifier, discardHelper, sourceMap, capture.SinkTemporary);
        builder.Append(prefix).Append(RenderStorage(target)).Append(" = ")
            .Append(capture.RecordsTemporary).AppendLine(".ToArray();");
    }
}
