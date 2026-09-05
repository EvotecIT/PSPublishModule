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
           command.CommandElements.Count == 1;

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
                $"{provider.CommandName} is compiled only with no arguments, parameters, invocation operator, or redirection.",
                span));
            return null;
        }

        return new PowerShellBoundRuntimeStateExpression(
            span,
            PowerShellRuntimeStateIntrinsicKind.CurrentLocalDateTime,
            targetFramework ?? string.Empty,
            semanticProfileId,
            Array.Empty<PowerShellBoundExpression>(),
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
        type = typeof(DateTime);
        return true;
    }

    private static bool IsCurrentLocalDateTimeProvider(PowerShellCompilationCommandProviderContract provider)
        => provider.Family == PowerShellCompilationCommandFamily.RuntimeState &&
           provider.Adapter.Operation.Equals(CurrentLocalDateTimeOperation, StringComparison.Ordinal);
}
