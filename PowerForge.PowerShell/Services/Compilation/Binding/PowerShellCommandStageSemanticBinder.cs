using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Builds one deterministic semantic stage for every command in a hosted pipeline.</summary>
internal static class PowerShellCommandStageSemanticBinder
{
    internal static PowerShellBoundCommandStage Bind(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellCommandSemanticRegistry registry)
    {
        var resolution = registry.Resolve(command.GetCommandName());
        var provider = resolution.Status == PowerShellCommandResolutionStatus.Resolved &&
                       IsSupportedByFamilyBinder(command, resolution.Contract!)
            ? resolution.Contract!
            : PowerShellHostedRegionCommandSemanticBinder.CreateContract(command.GetCommandName());
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        var symbols = BindPipelineSymbols(document, command, provider);
        return provider.Family switch
        {
            PowerShellCompilationCommandFamily.Projection => new PowerShellBoundProjectionCommandStage(span, provider, symbols),
            PowerShellCompilationCommandFamily.Filtering => new PowerShellBoundFilteringCommandStage(span, provider, symbols),
            PowerShellCompilationCommandFamily.Mapping => new PowerShellBoundMappingCommandStage(span, provider, symbols),
            PowerShellCompilationCommandFamily.Sorting => new PowerShellBoundSortingCommandStage(span, provider, symbols),
            _ => new PowerShellBoundHostedCommandStage(span, provider, symbols)
        };
    }

    private static bool IsSupportedByFamilyBinder(CommandAst command, PowerShellCompilationCommandProviderContract provider)
        => provider.Family switch
        {
            PowerShellCompilationCommandFamily.Stream => PowerShellStreamCommandSemanticBinder.IsSupported(command, provider),
            PowerShellCompilationCommandFamily.Projection => PowerShellProjectionCommandSemanticBinder.IsSupported(command),
            PowerShellCompilationCommandFamily.Filtering => PowerShellFilteringCommandSemanticBinder.IsSupported(command),
            PowerShellCompilationCommandFamily.Mapping => PowerShellMappingCommandSemanticBinder.IsSupported(command),
            PowerShellCompilationCommandFamily.Sorting => PowerShellSortingCommandSemanticBinder.IsSupported(command),
            PowerShellCompilationCommandFamily.HostedRegion => true,
            _ => false
        };

    private static PowerShellBoundPipelineSymbol[] BindPipelineSymbols(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider)
        => command.FindAll(static node =>
                node is VariableExpressionAst variable &&
                (variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) ||
                 variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase)),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .GroupBy(static variable => variable.VariablePath.UserPath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var variable = group.OrderBy(static item => item.Extent.StartOffset).First();
                var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
                return new PowerShellBoundPipelineSymbol(new PowerShellSymbolId(
                    PowerShellSymbolKind.PipelineVariable,
                    document.DocumentId,
                    variable.VariablePath.UserPath,
                    span,
                    provider.ProviderId + ":" + command.Extent.StartOffset + ":" + variable.VariablePath.UserPath));
            })
            .OrderBy(static symbol => symbol.Symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
}

internal static class PowerShellStreamCommandSemanticBinder
{
    internal static bool IsSupported(CommandAst command, PowerShellCompilationCommandProviderContract provider)
        => TryBind(command, provider, out _, out _);

    internal static bool TryBind(
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message)
    {
        kind = default;
        message = null!;
        if (command.Parent is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count != 1 ||
            command.Redirections.Count != 0)
            return false;
        if (provider.Family != PowerShellCompilationCommandFamily.Stream || !provider.Adapter.RuntimeFree)
            return false;
        switch (provider.Stream)
        {
            case "Success": kind = PowerShellStreamCommandKind.Success; break;
            case "Verbose": kind = PowerShellStreamCommandKind.Verbose; break;
            case "Debug": kind = PowerShellStreamCommandKind.Debug; break;
            case "Warning": kind = PowerShellStreamCommandKind.Warning; break;
            case "Information": kind = PowerShellStreamCommandKind.Information; break;
            case "Host": kind = PowerShellStreamCommandKind.Host; break;
            case "Error": kind = PowerShellStreamCommandKind.Error; break;
            default: return false;
        }

        var arguments = command.CommandElements.Skip(1).ToArray();
        var valueParameter = provider.Parameters.SingleOrDefault();
        if (valueParameter is null)
            return false;
        if (arguments.Length == 1 && valueParameter.Position == 0 && arguments[0] is ExpressionAst positional)
        {
            message = positional;
            return kind == PowerShellStreamCommandKind.Success ? IsRuntimeFreeSuccessValue(message) : IsProvablyNonEmptyMessage(message);
        }
        if (arguments.Length == 2 &&
            arguments[0] is CommandParameterAst parameter &&
            IsAcceptedParameter(parameter.ParameterName, valueParameter) &&
            arguments[1] is ExpressionAst named)
        {
            message = named;
            return kind == PowerShellStreamCommandKind.Success ? IsRuntimeFreeSuccessValue(message) : IsProvablyNonEmptyMessage(message);
        }
        return false;
    }

    private static bool IsAcceptedParameter(
        string parameterName,
        PowerShellCompilationCommandParameterContract contract)
        => parameterName.Equals(contract.Name, StringComparison.OrdinalIgnoreCase) ||
           contract.Aliases.Any(alias => parameterName.Equals(alias, StringComparison.OrdinalIgnoreCase));

    private static bool IsProvablyNonEmptyMessage(ExpressionAst message)
        => message is StringConstantExpressionAst { Value.Length: > 0 };

    private static bool IsRuntimeFreeSuccessValue(ExpressionAst value)
        => value is StringConstantExpressionAst or ConstantExpressionAst;
}

internal static class PowerShellProjectionCommandSemanticBinder
{
    private static readonly HashSet<string> SupportedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Property", "ExcludeProperty", "ExpandProperty", "First", "Last", "Skip", "SkipLast", "Unique", "Wait"
    };

    internal static bool IsSupported(CommandAst command)
        => PowerShellBoundedCommandShape.HasOnlyStaticArguments(command, SupportedParameters, allowScriptBlocks: false);
}

internal static class PowerShellFilteringCommandSemanticBinder
{
    private static readonly HashSet<string> SupportedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "FilterScript", "Property", "Value", "EQ", "NE", "GT", "GE", "LT", "LE", "Like", "NotLike", "Match", "NotMatch", "Contains", "NotContains", "In", "NotIn", "Is", "IsNot", "CaseSensitive"
    };

    internal static bool IsSupported(CommandAst command)
        => PowerShellBoundedCommandShape.HasOnlyStaticArguments(command, SupportedParameters, allowScriptBlocks: true);
}

internal static class PowerShellMappingCommandSemanticBinder
{
    private static readonly HashSet<string> SupportedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Process", "Begin", "End", "RemainingScripts", "MemberName", "ArgumentList", "Parallel", "ThrottleLimit", "TimeoutSeconds", "AsJob", "UseNewRunspace"
    };

    internal static bool IsSupported(CommandAst command)
        => PowerShellBoundedCommandShape.HasOnlyStaticArguments(command, SupportedParameters, allowScriptBlocks: true);
}

internal static class PowerShellSortingCommandSemanticBinder
{
    private static readonly HashSet<string> SupportedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Property", "Descending", "Unique", "CaseSensitive", "Culture", "Stable", "Top", "Bottom"
    };

    internal static bool IsSupported(CommandAst command)
        => PowerShellBoundedCommandShape.HasOnlyStaticArguments(command, SupportedParameters, allowScriptBlocks: true);
}

internal static class PowerShellHostedRegionCommandSemanticBinder
{
    internal static PowerShellCompilationCommandProviderContract CreateContract(string? commandName)
        => PowerShellCommandSemanticRegistry.HostedRegionContract(commandName ?? "<dynamic>");
}

internal static class PowerShellBoundedCommandShape
{
    internal static bool HasOnlyStaticArguments(CommandAst command, ISet<string> supportedParameters, bool allowScriptBlocks)
    {
        if (command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0)
            return false;
        foreach (var element in command.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst parameter)
            {
                if (!supportedParameters.Contains(parameter.ParameterName) ||
                    parameter.Argument is not null && !IsStaticValue(parameter.Argument, allowScriptBlocks))
                    return false;
                continue;
            }
            if (!IsStaticValue(element, allowScriptBlocks)) return false;
        }
        return true;
    }

    private static bool IsStaticValue(CommandElementAst element, bool allowScriptBlocks)
        => element is StringConstantExpressionAst or ConstantExpressionAst or VariableExpressionAst ||
           allowScriptBlocks && element is ScriptBlockExpressionAst;
}
