using System.Text;

namespace PowerForge;

/// <summary>Shares invocation context, stream and error wiring for functions and literal script blocks.</summary>
internal static class PowerShellNativeCallbackSource
{
    internal static string Callback(bool present, int clause)
        => present ? "context => Invoke(context, " + clause.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : "null";

    internal static void AppendBody(StringBuilder builder, string indentation, string callTarget, string sourceName,
        bool statementErrors, bool stopping, bool streams, bool returnsVoid, bool enumeratesCollection)
    {
        builder.Append(indentation).AppendLine("context.LifecycleClause = clause;");
        if (statementErrors || stopping)
            builder.Append(indentation).Append("using var statementErrors = global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.CreateNativeFunction(context.FunctionContext, ")
                .Append(PowerShellCSharpLiteral.QuoteString(sourceName)).AppendLine(");");
        var arguments = new List<string> { "context" };
        if (statementErrors) arguments.Add("statementErrors");
        if (stopping) arguments.Add("statementErrors.CheckLoopInterrupts");
        if (streams)
            arguments.AddRange(new[] { "context.WriteValue", "context.WriteVerbose", "context.WriteDebug", "context.WriteWarning",
                "context.WriteInformation", "context.WriteHost", "context.WriteError" });
        var call = callTarget + "(" + string.Join(", ", arguments) + ")";
        if (returnsVoid) builder.Append(indentation).Append(call).AppendLine(";");
        else if (enumeratesCollection) builder.Append(indentation).Append("foreach (var value in ").Append(call).AppendLine(") context.WriteValue(value);");
        else builder.Append(indentation).Append("context.WriteValue(").Append(call).AppendLine(");");
    }
}
