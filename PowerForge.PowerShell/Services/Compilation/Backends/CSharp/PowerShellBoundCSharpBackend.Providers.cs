using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static void EmitProviderStreamWrite(
        StringBuilder builder,
        PowerShellLoweredStreamWriteStatement stream,
        string prefix,
        Func<string, string> getTemporaryIdentifier)
    {
        var sink = stream.Kind switch
        {
            PowerShellStreamCommandKind.Success => "__writeOutput",
            PowerShellStreamCommandKind.Verbose => "__writeVerbose",
            PowerShellStreamCommandKind.Debug => "__writeDebug",
            PowerShellStreamCommandKind.Warning => "__writeWarning",
            PowerShellStreamCommandKind.Information => "__writeInformation",
            PowerShellStreamCommandKind.Host => "__writeHost",
            PowerShellStreamCommandKind.Error => "__writeError",
            _ => throw new InvalidOperationException($"Stream kind '{stream.Kind}' has no C# host binding.")
        };
        var entryPoint = stream.Provider.Adapter.EntryPoint;
        var convertedMessage = "global::System.Convert.ToString(" + EmitExpression(stream.Message) +
            ", global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty";
        var nullFailure = "new global::System.InvalidOperationException(" +
            PowerShellCSharpLiteral.QuoteString($"Provider '{stream.Provider.ProviderId}' returned a null value outside its contract.") + ")";
        if (entryPoint is not null && stream.Kind == PowerShellStreamCommandKind.Success &&
            stream.Provider.Cardinality == PowerShellCompilationCommandCardinality.Collection)
        {
            var item = getTemporaryIdentifier("providerItem");
            builder.Append(prefix).Append("foreach (string ").Append(item).Append(" in (global::")
                .Append(EscapeQualifiedProviderIdentifier(entryPoint.TypeName)).Append('.').Append(EscapeProviderIdentifier(entryPoint.MethodName)).Append('(')
                .Append(convertedMessage).Append(") ?? throw ").Append(nullFailure).AppendLine("))")
                .Append(prefix).AppendLine("{")
                .Append(prefix).Append("    if (").Append(item).Append(" is null) throw ").Append(nullFailure).AppendLine(";")
                .Append(prefix).Append("    ").Append(sink).Append("((object?)").Append(item).AppendLine(");")
                .Append(prefix).AppendLine("}");
            return;
        }
        builder.Append(prefix).Append(sink).Append('(');
        if (entryPoint is not null)
            builder.Append(stream.Kind == PowerShellStreamCommandKind.Success ? "(object?)" : string.Empty)
                .Append("(global::").Append(EscapeQualifiedProviderIdentifier(entryPoint.TypeName)).Append('.').Append(EscapeProviderIdentifier(entryPoint.MethodName)).Append('(')
                .Append(convertedMessage).Append(") ?? throw ").Append(nullFailure).Append(')');
        else if (stream.Kind == PowerShellStreamCommandKind.Success)
            builder.Append("(object?)").Append(EmitExpression(stream.Message));
        else
            builder.Append(convertedMessage);
        builder.AppendLine(");");
    }

    private static string EscapeQualifiedProviderIdentifier(string value)
        => string.Join(".", value.Split('.').Select(EscapeProviderIdentifier));

    private static string EscapeProviderIdentifier(string value) => "@" + value;
}
