using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeStateCommandSemanticBinder
{
    private const string CurrentLocalDateTimeOperation = "ReadCurrentLocalDateTime";

    internal static bool IsSupportedShape(
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics) &&
           IsCurrentLocalDateTimeProvider(provider) &&
           command.InvocationOperator == TokenKind.Unknown &&
           command.Redirections.Count == 0 &&
           (command.CommandElements.Count == 1 || TryGetLiteralFormat(command, out _));

    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        string? targetFramework,
        string semanticProfileId,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (!IsSupportedShape(command, provider, capabilities))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                $"{provider.CommandName} is compiled only without arguments or with one literal -Format value covered by the .NET date-format contract.",
                span));
            return null;
        }

        var formatted = TryGetLiteralFormat(command, out var format);
        var arguments = formatted
            ? new PowerShellBoundExpression[]
            {
                new PowerShellBoundLiteralExpression(
                    PowerShellSourceParser.GetSpan(document, command.CommandElements.Last().Extent),
                    format,
                    new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal,
                        "The Get-Date format is a syntax-owned literal."),
                    PowerShellValueState.Known)
            }
            : Array.Empty<PowerShellBoundExpression>();
        return new PowerShellBoundRuntimeStateExpression(
            span,
            formatted ? PowerShellRuntimeStateIntrinsicKind.FormattedCurrentLocalDateTime :
                PowerShellRuntimeStateIntrinsicKind.CurrentLocalDateTime,
            targetFramework ?? string.Empty,
            semanticProfileId,
            arguments,
            provider);
    }

    internal static bool TryGetResultType(
        CommandAst command,
        PowerShellCommandSemanticResolver resolver,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities,
        out Type type)
    {
        type = typeof(object);
        if (resolver.Resolve(command, localFunctionNames, capabilities) is not
            {
                IsProvider: true,
                Contract.Family: PowerShellCompilationCommandFamily.RuntimeState
            } resolution ||
            !IsSupportedShape(command, resolution.Contract!, capabilities))
            return false;
        type = command.CommandElements.Count == 1 ? typeof(DateTime) : typeof(string);
        return true;
    }

    private static bool TryGetLiteralFormat(CommandAst command, out string format)
    {
        format = string.Empty;
        if (command.CommandElements.Count is not (2 or 3) ||
            command.CommandElements[1] is not CommandParameterAst parameter ||
            !parameter.ParameterName.Equals("Format", StringComparison.OrdinalIgnoreCase))
            return false;
        var literal = command.CommandElements.Count == 2
            ? parameter.Argument as StringConstantExpressionAst
            : parameter.Argument is null ? command.CommandElements[2] as StringConstantExpressionAst : null;
        if (literal is null) return false;
        format = literal.Value;
        if (string.IsNullOrEmpty(format) || IsPowerShellNamedFormat(format)) return false;
        try
        {
            _ = DateTime.MinValue.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsPowerShellNamedFormat(string format)
        => format.Equals("FileDate", StringComparison.OrdinalIgnoreCase) ||
           format.Equals("FileDateUniversal", StringComparison.OrdinalIgnoreCase) ||
           format.Equals("FileDateTime", StringComparison.OrdinalIgnoreCase) ||
           format.Equals("FileDateTimeUniversal", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentLocalDateTimeProvider(PowerShellCompilationCommandProviderContract provider)
        => provider.Family == PowerShellCompilationCommandFamily.RuntimeState &&
           provider.Adapter.Operation.Equals(CurrentLocalDateTimeOperation, StringComparison.Ordinal);
}
