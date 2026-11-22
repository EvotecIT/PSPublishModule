using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellHostedBooleanCommandSemanticBinder
{
    private static readonly HashSet<string> PathTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Any", "Container", "Leaf"
    };

    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        Type? contextualType,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (contextualType != typeof(bool) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                $"{provider.CommandName} is compiled only as a Boolean expression inside a generated PowerShell host.",
                span));
            return null;
        }
        if (!provider.ProviderId.Equals("powerforge.command.hosted-boolean.test-path", StringComparison.Ordinal) ||
            !TryGetTestPathShape(command, out var pathParameter, out var pathSyntax, out var pathTypeSyntax, out var isValid, out var errorActionSyntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                "Boolean Test-Path requires one scalar -LiteralPath/-PSPath value, explicit -ErrorAction Ignore, optional literal -PathType Any, Container, or Leaf, and optional -IsValid.",
                span));
            return null;
        }
        if (!PowerShellCommandIslandPolicy.IsSafeHostedProviderPath(pathSyntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                "Boolean Test-Path requires an expression proven to begin with FileSystem:: because compiled locals are not PowerShell provider session state.",
                span));
            return null;
        }

        var path = bindExpression(pathSyntax, null);
        if (path is null || path.Type.ClrType != typeof(string))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                "Boolean Test-Path requires one statically typed scalar string path; collection and coercive path binding remain hosted or rejected.",
                span));
            return null;
        }
        var arguments = new List<PowerShellBoundHostedCommandArgument>
        {
            new(pathParameter, path)
        };
        if (pathTypeSyntax is not null)
        {
            var pathType = bindExpression(pathTypeSyntax, null);
            if (pathType is null || pathType.Type.ClrType != typeof(string)) return null;
            arguments.Add(new PowerShellBoundHostedCommandArgument("PathType", pathType));
        }
        if (isValid)
            arguments.Add(new PowerShellBoundHostedCommandArgument("IsValid", null));
        var errorAction = bindExpression(errorActionSyntax, null);
        if (errorAction is null || errorAction.Type.ClrType != typeof(string)) return null;
        arguments.Add(new PowerShellBoundHostedCommandArgument("ErrorAction", errorAction));
        return new PowerShellBoundHostedBooleanCommandExpression(span, provider, arguments);
    }

    private static bool TryGetTestPathShape(
        CommandAst command,
        out string pathParameter,
        out ExpressionAst path,
        out ExpressionAst? pathType,
        out bool isValid,
        out ExpressionAst errorAction)
    {
        pathParameter = string.Empty;
        path = null!;
        pathType = null;
        isValid = false;
        errorAction = null!;
        if (command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0)
            return false;

        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst parameter)
            {
                if (parameter.ParameterName.Equals("IsValid", StringComparison.OrdinalIgnoreCase))
                {
                    if (isValid || parameter.Argument is not null) return false;
                    isValid = true;
                    continue;
                }

                var argument = parameter.Argument;
                if (argument is null && index + 1 < command.CommandElements.Count)
                    argument = command.CommandElements[++index] as ExpressionAst;
                if (argument is null) return false;
                if (parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase) ||
                    parameter.ParameterName.Equals("PSPath", StringComparison.OrdinalIgnoreCase))
                {
                    if (path is not null) return false;
                    pathParameter = "LiteralPath";
                    path = argument;
                }
                else if (parameter.ParameterName.Equals("PathType", StringComparison.OrdinalIgnoreCase))
                {
                    if (pathType is not null || argument is not StringConstantExpressionAst literal || !PathTypes.Contains(literal.Value))
                        return false;
                    pathType = argument;
                }
                else if (parameter.ParameterName.Equals("ErrorAction", StringComparison.OrdinalIgnoreCase) ||
                         parameter.ParameterName.Equals("EA", StringComparison.OrdinalIgnoreCase))
                {
                    if (errorAction is not null || argument is not StringConstantExpressionAst literal ||
                        !literal.Value.Equals("Ignore", StringComparison.OrdinalIgnoreCase))
                        return false;
                    errorAction = argument;
                }
                else return false;
                continue;
            }

            return false;
        }
        return path is not null && errorAction is not null;
    }
}
