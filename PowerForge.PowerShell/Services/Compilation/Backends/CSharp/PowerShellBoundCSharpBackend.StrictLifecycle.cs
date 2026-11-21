using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private bool _emittingStrictPipelineClause;

    private bool TryEmitStrictPipelineStage(StringBuilder builder, PowerShellLoweredFunction function, int indent,
        Func<string, string> allocate, string? discardHelper, ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var attachment = function.Parameters.LastOrDefault();
        if (attachment is null || !PowerShellTypedLowerer.IsStrictStageType(attachment.ClrType)) return false;

        var localCount = function.Statements.TakeWhile(static statement => statement is PowerShellLoweredLocalDeclarationStatement).Count();
        foreach (var declaration in function.Statements.Take(localCount))
            EmitStatement(builder, declaration, indent, allocate, discardHelper, sourceMap);
        var body = function.Statements.Skip(localCount).ToArray();
        IEnumerable<PowerShellLoweredStatement> cleanup = Array.Empty<PowerShellLoweredStatement>();
        if (body.Length == 1 && body[0] is PowerShellLoweredTryStatement { Catches.Length: 0, FinallyStatements: not null } guarded)
        {
            cleanup = guarded.FinallyStatements!;
            body = guarded.Statements.ToArray();
        }
        var parameterKeys = function.Parameters.Select(static parameter => parameter.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        var processIndex = Array.FindIndex(body, statement => statement is PowerShellLoweredForEachStatement
        {
            Collection: PowerShellLoweredVariableExpression collection,
            EnumerationKind: PowerShellForEachEnumerationKind.TypedArray
        } && parameterKeys.Contains(collection.Symbol.StableKey));
        if (processIndex < 0)
            throw new InvalidOperationException("A composed lifecycle stage has no typed process iteration.");
        var process = (PowerShellLoweredForEachStatement)body[processIndex];
        var returnsValue = function.ReturnType != typeof(void);
        var prefix = new string(' ', indent * 4);
        var stage = allocate("pipelineStage");
        var record = allocate("pipelineRecord");
        var attachmentName = RenderStorage(attachment.Symbol);
        var limit = RenderStorage(function.Parameters[function.Parameters.Length - 2].Symbol);
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

        _emittingStrictPipelineClause = true;
        try
        {
            EmitClause("()", body.Take(processIndex), stage + ".Output", ",");
            EmitClause(record, process.Statements, stage + ".Output", ",", RenderStorage(process.Variable) + " = " + record + ";");
            EmitClause("()", body.Skip(processIndex + 1), stage + ".Output", ",");
            EmitClause("()", cleanup, stage + ".Output", ");");
        }
        finally
        {
            _emittingStrictPipelineClause = false;
        }

        if (returnsValue)
        {
            builder.Append(prefix).AppendLine("return default!;");
            indent--;
            builder.Append(new string(' ', indent * 4)).AppendLine("}");
            foreach (var statement in function.Statements.Skip(localCount))
                EmitStatement(builder, statement, indent, allocate, discardHelper, sourceMap);
            return true;
        }

        var nullRecord = process.NullCollectionElement is null ? "default!" : EmitExpression(process.NullCollectionElement);
        builder.Append(prefix).Append("if (").Append(attachmentName).AppendLine(" is null)");
        builder.Append(prefix).Append("    global::PowerForge.Generated.Runtime.PowerShellStrictPipelineComposition.RunSingle(")
            .Append(EmitExpression(process.Collection)).Append(", ")
            .Append(process.NullCollectionElement is null ? "false, " : "true, ").Append(nullRecord).Append(", ")
            .Append(stage).Append(", ").Append(limit).AppendLine(", __writeOutput);");
        return true;

        void EmitClause(string parameters, IEnumerable<PowerShellLoweredStatement> statements, string sink, string separator,
            string? bindRecord = null)
        {
            builder.Append(prefix).Append("    ").Append(parameters).AppendLine(" =>");
            builder.Append(prefix).AppendLine("    {");
            if (bindRecord is not null)
                builder.Append(prefix).Append("        ").AppendLine(bindRecord);
            foreach (var statement in statements)
            {
                if (returnsValue && statement is PowerShellLoweredReturnStatement { EmitsValue: true, Expression: not null } terminal)
                    builder.Append(prefix).Append("        ").Append(sink).Append("((object?)")
                        .Append(EmitExpression(terminal.Expression!)).AppendLine(");");
                else
                    EmitStatement(builder, statement, indent + 2, allocate, discardHelper, sourceMap, sink);
            }
            builder.Append(prefix).Append("    }").AppendLine(separator);
        }
    }
}
