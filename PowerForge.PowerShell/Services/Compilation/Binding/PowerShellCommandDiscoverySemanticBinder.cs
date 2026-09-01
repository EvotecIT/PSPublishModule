using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCommandDiscoverySemanticBinder
{
    internal static bool IsSupportedBooleanConsumption(
        CommandAst command,
        PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
           IsBooleanConsumption(command) &&
           TryGetShape(command, out _, out _);

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
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                "Get-Command is compiled only as Boolean command availability inside a generated PowerShell host.",
                span));
            return null;
        }
        if (!TryGetShape(command, out var nameSyntax, out var errorAction))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                provider.FeatureId,
                "Boolean Get-Command discovery requires one -Name/positional name and -ErrorAction Ignore or SilentlyContinue.",
                span));
            return null;
        }
        var name = bindExpression(nameSyntax, typeof(string));
        return name is null ? null : new PowerShellBoundCommandAvailabilityExpression(span, name, errorAction, provider);
    }

    private static bool TryGetShape(
        CommandAst command,
        out ExpressionAst name,
        out PowerShellCommandDiscoveryErrorAction errorAction)
    {
        name = null!;
        errorAction = default;
        if (command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0)
            return false;
        var hasErrorAction = false;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst parameter)
            {
                var argument = parameter.Argument;
                if (argument is null && index + 1 < command.CommandElements.Count)
                    argument = command.CommandElements[++index] as ExpressionAst;
                if (argument is null) return false;
                if (parameter.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    if (name is not null) return false;
                    name = argument;
                }
                else if (parameter.ParameterName.Equals("ErrorAction", StringComparison.OrdinalIgnoreCase) ||
                         parameter.ParameterName.Equals("EA", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasErrorAction || argument is not StringConstantExpressionAst action ||
                        !action.Value.Equals("Ignore", StringComparison.OrdinalIgnoreCase) &&
                        !action.Value.Equals("SilentlyContinue", StringComparison.OrdinalIgnoreCase))
                        return false;
                    hasErrorAction = true;
                    errorAction = action.Value.Equals("Ignore", StringComparison.OrdinalIgnoreCase)
                        ? PowerShellCommandDiscoveryErrorAction.Ignore
                        : PowerShellCommandDiscoveryErrorAction.SilentlyContinue;
                }
                else return false;
                continue;
            }
            if (name is not null || command.CommandElements[index] is not ExpressionAst positional)
                return false;
            name = positional;
        }
        return name is not null && hasErrorAction;
    }

    private static bool IsBooleanConsumption(CommandAst command)
    {
        for (Ast current = command; current.Parent is not null; current = current.Parent)
        {
            if (current.Parent is ConvertExpressionAst conversion && conversion.StaticType == typeof(bool)) return true;
            if (current.Parent is IfStatementAst conditional && conditional.Clauses.Any(clause => ReferenceEquals(clause.Item1, current))) return true;
            if (current.Parent is WhileStatementAst loop && ReferenceEquals(loop.Condition, current)) return true;
            if (current.Parent is ForStatementAst forLoop && ReferenceEquals(forLoop.Condition, current)) return true;
            if (current.Parent is PipelineAst or CommandExpressionAst or ParenExpressionAst) continue;
            return false;
        }
        return false;
    }
}
