using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitPowerShellForEach(StringBuilder builder, PowerShellLoweredForEachStatement loop,
        string collection, int indent, Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap, string? successOutputSink)
    {
        var prefix = new string(' ', indent * 4);
        var source = getTemporaryIdentifier("foreachSource");
        var cursor = getTemporaryIdentifier("foreachCursor");
        builder.Append(prefix).Append("var ").Append(source).Append(" = ").Append(collection).AppendLine(";");
        builder.Append(prefix).Append("if (").Append(source).AppendLine(" is not null)");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    var ").Append(cursor).Append(" = __statementErrors.GetEnumerator(").Append(source).AppendLine(");");
        // Native foreach treats a non-null value with no enumerator as one scalar.
        // It does not implicitly dispose the enumerator on completion or unwinding.
        builder.Append(prefix).Append("    ").Append(cursor).Append(" ??= new object?[] { ").Append(source).AppendLine(" }.GetEnumerator();");
        builder.Append(prefix).Append("    while (__statementErrors.MoveEnumerator(").Append(cursor).AppendLine("))");
        builder.Append(prefix).AppendLine("    {");
        EmitForEachBody(builder, loop, "__statementErrors.ReadEnumeratorCurrent(" + cursor + ")!",
            indent + 1, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        builder.Append(prefix).AppendLine("    }");
        builder.Append(prefix).AppendLine("}");
    }
}
