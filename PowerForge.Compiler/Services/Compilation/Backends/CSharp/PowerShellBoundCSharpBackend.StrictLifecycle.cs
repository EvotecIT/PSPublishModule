using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static void AppendDirectLifecycleOverload(StringBuilder builder, PowerShellLoweredFunction function,
        IReadOnlyList<string> parameterParts)
    {
        var attachmentIndex = function.Parameters.Length - 1;
        var visible = parameterParts.Where((_, index) => index != attachmentIndex).ToArray();
        var arguments = visible.Select(static parameter => parameter.Substring(parameter.LastIndexOf(' ') + 1)).ToList();
        arguments.Insert(attachmentIndex, "null");
        builder.AppendLine().Append("    public static ")
            .Append(PowerShellCSharpSymbolRenderer.TypeName(function.ReturnType)).Append(' ')
            .Append(function.GeneratedName).Append('(').Append(string.Join(", ", visible)).Append(") => ")
            .Append(function.GeneratedName).Append('(').Append(string.Join(", ", arguments)).Append(");");
    }

    private bool TryEmitStrictPipelineStage(StringBuilder builder, PowerShellLoweredFunction function, int indent,
        Func<string, string> allocate, string? discardHelper, ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        if (function.StrictLifecycle is not { } lifecycle) return false;
        var attachment = function.Parameters.Single(parameter => parameter.Symbol.Equals(lifecycle.Attachment));
        foreach (var declaration in lifecycle.SharedLocals)
            EmitStatement(builder, declaration, indent, allocate, discardHelper, sourceMap);
        var process = lifecycle.Process;
        var returnsValue = function.ReturnType != typeof(void);
        var prefix = new string(' ', indent * 4);
        var stage = allocate("pipelineStage");
        var record = allocate("pipelineRecord");
        var attachmentName = RenderStorage(attachment.Symbol);
        var limit = lifecycle.Limit is { } limitSymbol ? RenderStorage(limitSymbol) : "0";
        if (returnsValue)
        {
            builder.Append(prefix).Append("if (").Append(attachmentName).AppendLine(" is not null)");
            builder.Append(prefix).AppendLine("{");
            indent++;
            prefix += "    ";
            builder.Append(prefix).Append("var ").Append(stage).Append(" = ").Append(attachmentName).AppendLine(";");
        }
        else
        {
            builder.Append(prefix).Append("var ").Append(stage).Append(" = ").Append(attachmentName).Append(" ?? new ")
                .Append(PowerShellCSharpSymbolRenderer.TypeName(attachment.ClrType)).AppendLine("();");
        }
        builder.Append(prefix).Append(stage).AppendLine(".Configure(");

        EmitClause("()", lifecycle.Begin, stage + ".Output", ",");
        EmitClause(record, process.Statements, stage + ".Output", ",", RenderStorage(process.Variable) + " = " + record + ";");
        EmitClause("()", lifecycle.End, stage + ".Output", ",");
        EmitClause("()", lifecycle.Cleanup, stage + ".Output", ");");

        if (returnsValue)
        {
            builder.Append(prefix).AppendLine("return default!;");
            indent--;
            builder.Append(new string(' ', indent * 4)).AppendLine("}");
            foreach (var statement in lifecycle.DirectBody)
                EmitStatement(builder, statement, indent, allocate, discardHelper, sourceMap);
            return true;
        }

        var nullRecord = process.NullCollectionElement is null ? "default!" : EmitExpression(process.NullCollectionElement);
        builder.Append(prefix).Append("if (").Append(attachmentName).AppendLine(" is null)");
        builder.Append(prefix).Append("    global::PowerForge.Generated.Runtime.PowerShellStrictPipelineComposition.RunSingle(")
            .Append(EmitExpression(process.Collection)).Append(", ")
            .Append(process.NullCollectionElement is null ? "false, " : "true, ").Append(nullRecord).Append(", ")
            .Append(stage).Append(", ").Append(limit)
            .AppendLine(function.RequiresPowerShellStreams ? ", __writeOutput);" : ", static _ => { });");
        return true;

        void EmitClause(string parameters, IEnumerable<PowerShellLoweredStatement> statements, string sink, string separator,
            string? bindRecord = null)
        {
            builder.Append(prefix).Append("    ").Append(parameters).AppendLine(" =>");
            builder.Append(prefix).AppendLine("    {");
            if (bindRecord is not null)
                builder.Append(prefix).Append("        ").AppendLine(bindRecord);
            foreach (var statement in statements)
                EmitStatement(builder, statement, indent + 2, allocate, discardHelper, sourceMap, sink);
            builder.Append(prefix).Append("    }").AppendLine(separator);
        }
    }
}
