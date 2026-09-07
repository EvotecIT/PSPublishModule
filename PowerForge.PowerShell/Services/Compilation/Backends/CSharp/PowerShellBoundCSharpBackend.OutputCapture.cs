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
        EmitBlock(builder, capture.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
        builder.Append(prefix).Append("finally { __writeOutput = ").Append(previous).AppendLine("; }");
        builder.Append(prefix).Append(PowerShellCSharpSymbolRenderer.Identifier(capture.Target.Name)).Append(" = ")
            .Append(records).Append(".Count == 0 ? global::System.Management.Automation.Internal.AutomationNull.Value : ").Append(records).Append(".Count == 1 ? ")
            .Append(records).Append("[0] : ").Append(records).AppendLine(".ToArray();");
    }
}
