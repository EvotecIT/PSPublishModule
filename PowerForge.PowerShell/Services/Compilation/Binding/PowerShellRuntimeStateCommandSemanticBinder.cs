using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeStateCommandSemanticBinder
{
    internal const string PlainConsoleInputProviderId = "powerforge.command.runtime-state.read-host";
    private const string CurrentLocalDateTimeOperation = "ReadCurrentLocalDateTime";

    internal static bool IsSupportedShape(
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellCompilationCapability capabilities,
        bool permitsGeneratedScriptRoot = false)
        => capabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics) &&
           command.InvocationOperator == TokenKind.Unknown &&
           command.Redirections.Count == 0 &&
           (IsCurrentLocalDateTimeProvider(provider) && (command.CommandElements.Count == 1 || TryGetLiteralFormat(command, out _)) ||
            IsPlainConsoleInput(provider) && capabilities == PowerShellCompilationCapabilities.TypedExecutable &&
            (permitsGeneratedScriptRoot || IsInScriptRoot(command)) &&
            TryGetLiteralPrompt(command, out _) && TryGetConsoleErrorRoute(command, out _));

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
        if (IsPlainConsoleInput(provider))
        {
            if (!IsSupportedShape(command, provider, capabilities, document.AuthoredProjection is not null) || !TryGetLiteralPrompt(command, out var prompt))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(provider.FeatureId,
                    "Strict Read-Host requires a direct script-root assignment with an optional literal prompt.", span));
                return null;
            }
            var source = document.AuthoredProjection?.Select(document, new[] { command.Extent }) ??
                new PowerShellCommandRegionSourceSelection(document.Path, document.Text, new[] { span });
            var authored = source.Statements.Single();
            var line = source.Document.Split('\n')[authored.StartLine - 1].TrimEnd('\r');
            var consoleArguments = new object[]
            {
                prompt, Path.GetFileName(source.Path), authored.StartLine, authored.StartColumn,
                authored.StartLine == authored.EndLine ? authored.EndColumn - authored.StartColumn : line.Length - authored.StartColumn + 1,
                line, command.GetCommandName()!
            };
            return new PowerShellBoundRuntimeStateExpression(span, PowerShellRuntimeStateIntrinsicKind.PlainConsoleInput,
                targetFramework ?? string.Empty, semanticProfileId, consoleArguments.Select(value => new PowerShellBoundLiteralExpression(
                    span, value, new PowerShellTypeFact(value.GetType(), PowerShellTypeFactProvenance.Literal, "The command source determines this argument."),
                    PowerShellValueState.Known)).ToArray(), provider);
        }

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
        type = IsCurrentLocalDateTimeProvider(resolution.Contract!) && command.CommandElements.Count == 1
            ? typeof(DateTime)
            : typeof(string);
        return true;
    }

    private static bool IsPlainConsoleInput(PowerShellCompilationCommandProviderContract provider)
        => provider.ProviderId.Equals(PlainConsoleInputProviderId, StringComparison.Ordinal);

    private static bool IsInScriptRoot(CommandAst command)
    {
        for (Ast? parent = command.Parent; parent is not null; parent = parent.Parent)
            if (parent is ScriptBlockAst block) return block.Parent is null;
        return false;
    }

    internal static bool TryGetConsoleErrorRoute(CommandAst command, out bool catchesStatementError)
    {
        catchesStatementError = false;
        if (command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) ||
            pipeline.Parent is not AssignmentStatementAst { Operator: TokenKind.Equals } assignment ||
            PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is not { VariablePath.IsUnqualified: true })
            return false;
        var requiresFinallyUnwind = false;
        for (Ast? child = assignment, parent = assignment.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is CatchClauseAst) return false;
            if (parent is not TryStatementAst attempt) continue;
            if (ReferenceEquals(child, attempt.Finally)) return false;
            if (ReferenceEquals(child, attempt.Body))
            {
                if (attempt.CatchClauses.Count == 0)
                {
                    requiresFinallyUnwind = true;
                    continue;
                }
                if (attempt.CatchClauses.Any(static clause => clause.CatchTypes.Count != 0)) return false;
                catchesStatementError = true;
                return true;
            }
        }
        return !requiresFinallyUnwind;
    }

    private static bool TryGetLiteralPrompt(CommandAst command, out string prompt)
    {
        prompt = string.Empty;
        var elements = command.CommandElements;
        if (elements.Count == 1) return true;
        var value = elements.Count switch
        {
            2 when elements[1] is StringConstantExpressionAst literal => literal,
            2 when elements[1] is CommandParameterAst parameter && parameter.ParameterName.Equals("Prompt", StringComparison.OrdinalIgnoreCase)
                => parameter.Argument as StringConstantExpressionAst,
            3 when elements[1] is CommandParameterAst { Argument: null } parameter && parameter.ParameterName.Equals("Prompt", StringComparison.OrdinalIgnoreCase)
                => elements[2] as StringConstantExpressionAst,
            _ => null
        };
        if (value is null) return false;
        prompt = value.Value;
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
