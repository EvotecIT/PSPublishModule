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
        var cancellationArgument = stream.Provider.Adapter.Cancellation is
            PowerShellCompilationProviderCancellation.Cooperative or
            PowerShellCompilationProviderCancellation.PostInitializationCooperative
            ? ", __providerCancellationToken"
            : string.Empty;
        var providerInvocation = entryPoint is null
            ? string.Empty
            : stream.Provider.Adapter.Cancellation == PowerShellCompilationProviderCancellation.ProcessIsolated
                ? "global::PowerForge.Compiled.PowerForgeProviderProcessIsolation.Invoke(" +
                  PowerShellCSharpLiteral.QuoteString(stream.Provider.ProviderId) + ", " + convertedMessage + ", " +
                  stream.Provider.Adapter.ProcessIsolationTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  ", __providerCancellationToken)"
                : "global::" + EscapeQualifiedProviderIdentifier(entryPoint.TypeName) + "." +
                  EscapeProviderIdentifier(entryPoint.MethodName) + "(" + convertedMessage + cancellationArgument + ")";
        var nullFailure = "new global::System.InvalidOperationException(" +
            PowerShellCSharpLiteral.QuoteString($"Provider '{stream.Provider.ProviderId}' returned a null value outside its contract.") + ")";
        if (entryPoint is not null && stream.Kind == PowerShellStreamCommandKind.Success &&
            stream.Provider.Cardinality == PowerShellCompilationCommandCardinality.Collection)
        {
            var item = getTemporaryIdentifier("providerItem");
            builder.Append(prefix).Append("foreach (").Append(GetProviderResultTypeName(entryPoint.ResultType)).Append(' ')
                .Append(item).Append(" in (").Append(providerInvocation)
                .Append(" ?? throw ").Append(nullFailure).AppendLine("))")
                .Append(prefix).AppendLine("{");
            if (entryPoint.ResultType == PowerShellCompilationProviderValueType.String)
                builder.Append(prefix).Append("    if (").Append(item).Append(" is null) throw ").Append(nullFailure).AppendLine(";");
            builder.Append(prefix).Append("    ").Append(sink).Append("((object?)").Append(item).AppendLine(");")
                .Append(prefix).AppendLine("}");
            return;
        }
        builder.Append(prefix).Append(sink).Append('(');
        if (entryPoint is not null)
        {
            var nullableResult = entryPoint.ResultType == PowerShellCompilationProviderValueType.String;
            if (stream.Kind == PowerShellStreamCommandKind.Success)
                builder.Append(nullableResult ? "(object?)(" : "(object?)");
            else if (nullableResult)
                builder.Append('(');
            builder.Append(providerInvocation);
            if (nullableResult)
                builder.Append(" ?? throw ").Append(nullFailure).Append(')');
        }
        else if (stream.Kind == PowerShellStreamCommandKind.Success)
            builder.Append("(object?)").Append(EmitExpression(stream.Message));
        else
            builder.Append(convertedMessage);
        builder.AppendLine(");");
    }

    private static string EscapeQualifiedProviderIdentifier(string value)
        => string.Join(".", value.Split('.').Select(EscapeProviderIdentifier));

    private static string EscapeProviderIdentifier(string value) => "@" + value;

    private static string GetProviderResultTypeName(PowerShellCompilationProviderValueType valueType)
        => valueType switch
        {
            PowerShellCompilationProviderValueType.String => "string",
            PowerShellCompilationProviderValueType.Int32 => "int",
            PowerShellCompilationProviderValueType.Int64 => "long",
            PowerShellCompilationProviderValueType.Double => "double",
            PowerShellCompilationProviderValueType.Boolean => "bool",
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Provider result type is not defined.")
        };
}
