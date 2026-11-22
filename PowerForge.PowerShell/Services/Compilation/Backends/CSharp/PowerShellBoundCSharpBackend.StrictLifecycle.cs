using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private PowerShellSymbolId? _strictStageCollector;
    private (PowerShellLoweredForEachStatement Loop, string IsBound)? _strictPipelineParameterBinding;

    private string EmitParameterPresence(PowerShellLoweredParameterPresenceExpression presence)
        => _strictPipelineParameterBinding is { } binding &&
           string.Equals(presence.ParameterName, binding.Loop.Variable.Name, StringComparison.OrdinalIgnoreCase)
            ? binding.IsBound
            : $"__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(presence.ParameterName)})";

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
        // Retain the existing array-returning method for direct use. The attached
        // path forwards each addition to its consumer at the same position.
        var collector = function.ReturnType.IsArray &&
            body[body.Length - 1] is PowerShellLoweredReturnStatement
            {
                Expression: PowerShellLoweredClrInvocationExpression
                { MemberName: nameof(List<int>.ToArray), Receiver: PowerShellLoweredVariableExpression collected }
            } && body[0] is PowerShellLoweredAssignmentStatement setup && setup.Target.Equals(collected.Symbol)
            ? collected.Symbol : null;
        var begin = collector is null ? body.Take(processIndex) : body.Skip(1).Take(processIndex - 1);
        var end = collector is null ? body.Skip(processIndex + 1) : body.Skip(processIndex + 1).Take(body.Length - processIndex - 2);
        var prefix = new string(' ', indent * 4);
        // A pipeline parameter first becomes visible when a record enters process,
        // and remains visible to end and clean after that record.
        if (PowerShellLoweredTreeEnumerator.EnumerateExpressions(function.Statements)
            .OfType<PowerShellLoweredParameterPresenceExpression>()
            .Any(presence => string.Equals(presence.ParameterName, process.Variable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var isBound = allocate("pipelineParameterBound");
            _strictPipelineParameterBinding = (process, isBound);
            builder.Append(prefix).Append("var ").Append(isBound).AppendLine(" = false;");
        }
        var stage = allocate("pipelineStage");
        var record = allocate("pipelineRecord");
        var attachmentName = RenderStorage(attachment.Symbol);
        var limitParameter = function.Parameters.FirstOrDefault(static parameter =>
            parameter.Symbol.StableKey.EndsWith("/PIPELINE-OUTPUT-LIMIT", StringComparison.Ordinal));
        var limit = limitParameter is null ? "0" : RenderStorage(limitParameter.Symbol);
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

        _strictStageCollector = collector;
        try
        {
            EmitClause("()", begin, stage + ".Output", ",");
            EmitClause(record, process.Statements, stage + ".Output", ",", RenderStorage(process.Variable) + " = " + record + ";");
            EmitClause("()", end, stage + ".Output", ",");
            EmitClause("()", cleanup, stage + ".Output", ");");
        }
        finally
        {
            _strictStageCollector = null;
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
            .Append(stage).Append(", ").Append(limit)
            .AppendLine(function.RequiresPowerShellStreams ? ", __writeOutput);" : ", static _ => { });");
        return true;

        void EmitClause(string parameters, IEnumerable<PowerShellLoweredStatement> statements, string sink, string separator,
            string? bindRecord = null)
        {
            builder.Append(prefix).Append("    ").Append(parameters).AppendLine(" =>");
            builder.Append(prefix).AppendLine("    {");
            if (bindRecord is not null)
            {
                builder.Append(prefix).Append("        ").AppendLine(bindRecord);
                if (_strictPipelineParameterBinding is { } binding)
                    builder.Append(prefix).Append("        ").Append(binding.IsBound).AppendLine(" = true;");
            }
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
