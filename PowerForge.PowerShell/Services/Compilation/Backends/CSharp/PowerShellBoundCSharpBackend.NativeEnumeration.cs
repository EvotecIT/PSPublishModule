using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitNativeForEach(StringBuilder builder, PowerShellLoweredForEachStatement loop,
        string collection, int indent, Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var binding = loop.NativeBinding ?? throw new InvalidOperationException("Native foreach binding is missing.");
        var prefix = new string(' ', indent * 4);
        var scope = getTemporaryIdentifier("nativeForeach");
        builder.Append(prefix).Append("using (var ").Append(scope).AppendLine(" = __nativeFunction.EnterForEach())");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    ").Append(scope).Append(".Initialize(")
            .Append(EmitNativeExpressionPosition(collection, binding.CollectionSpan, binding.Target.SourcePath, binding.CollectionSourceText))
            .AppendLine(");");
        builder.Append(prefix).Append("    while (").Append(scope).Append(".Cursor is not null && ")
            .Append(EmitNativeExpressionPosition(scope + ".Cursor.MoveNext()", binding.Target.Span,
                binding.Target.SourcePath, binding.VariableSourceText)).AppendLine(")");
        builder.Append(prefix).AppendLine("    {");
        builder.Append(prefix).AppendLine("        __checkLoopInterrupts();");
        builder.Append(prefix).Append("        ").Append(EmitNativeAssignmentStart(binding.Target, PowerShellBoundMutationOperator.Assign))
            .Append("() => ").Append(scope).Append(".Cursor.Current").Append(EmitNativeAssignmentLocation(binding.Target)).AppendLine(";");
        foreach (var statement in loop.Statements)
            EmitStatement(builder, statement, indent + 2, getTemporaryIdentifier, discardHelper, sourceMap);
        builder.Append(prefix).AppendLine("    }");
        builder.Append(prefix).AppendLine("}");
    }
}
