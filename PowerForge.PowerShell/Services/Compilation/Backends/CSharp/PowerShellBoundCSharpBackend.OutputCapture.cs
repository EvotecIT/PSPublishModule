using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitOutputCapture(StringBuilder builder, PowerShellLoweredOutputCaptureStatement capture,
        int indent, Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
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
        foreach (var statement in capture.Statements)
            EmitStatement(builder, statement, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap);
        builder.Append(prefix).Append("    ");
        if (capture.UsesNativeInvocation)
            builder.Append("__nativeFunction.SetVariable(").Append(PowerShellCSharpLiteral.QuoteString(capture.Target.Name)).Append(", ");
        else
            builder.Append(PowerShellCSharpSymbolRenderer.Identifier(capture.Target.Name)).Append(" = ");
        builder
            .Append(records).Append(".Count == 0 ? global::System.Management.Automation.Internal.AutomationNull.Value : ").Append(records).Append(".Count == 1 ? ")
            .Append(records).Append("[0] : ").Append(records).Append(".ToArray()")
            .AppendLine(capture.UsesNativeInvocation ? ");" : ";");
        builder.Append(prefix).Append("    ").Append(records).AppendLine(".Clear();");
        builder.Append(prefix).AppendLine("}");
        // PowerShell discards assignment output for RuntimeException, but flushes
        // pending records when a raw CLR exception escapes (for example MoveNext).
        builder.Append(prefix).Append("catch (global::System.Management.Automation.RuntimeException) { ")
            .Append(records).AppendLine(".Clear(); throw; }");
        var flushedRecord = getTemporaryIdentifier("flushedCaptureRecord");
        builder.Append(prefix).AppendLine("finally");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    __writeOutput = ").Append(previous).AppendLine(";");
        builder.Append(prefix).Append("    foreach (var ").Append(flushedRecord).Append(" in ").Append(records)
            .Append(") ").Append(previous).Append('(').Append(flushedRecord).AppendLine(");");
        builder.Append(prefix).AppendLine("}");
    }
}
