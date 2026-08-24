using System.Management.Automation.Language;

namespace PowerForge;

internal enum PowerShellStreamCommandKind
{
    Verbose,
    Debug,
    Warning
}

internal static class PowerShellCommandIslandPolicy
{
    internal static bool IsRuntimeRegion(PipelineAst pipeline, ScriptBlockAst body)
    {
        if (!ReferenceEquals(pipeline.Parent, body.EndBlock) ||
            pipeline.PipelineElements.Count == 0 ||
            !pipeline.PipelineElements.OfType<CommandAst>().Any() ||
            pipeline.PipelineElements.OfType<CommandAst>().Any(static command => command.Redirections.Count != 0))
            return false;
        if (pipeline.PipelineElements.Count == 1 &&
            pipeline.PipelineElements[0] is CommandAst stream &&
            TryGetStreamCommand(stream, out _, out _))
            return false;

        var parameters = body.ParamBlock?.Parameters
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return pipeline.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .All(variable =>
            {
                var name = variable.VariablePath.UserPath;
                return parameters.Contains(name) ||
                       name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                       IsNestedPipelineVariable(variable, pipeline, name);
            });
    }

    private static bool IsNestedPipelineVariable(
        VariableExpressionAst variable,
        PipelineAst pipeline,
        string name)
    {
        if (!name.Equals("_", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("PSItem", StringComparison.OrdinalIgnoreCase))
            return false;

        for (Ast? ancestor = variable.Parent;
             ancestor is not null && !ReferenceEquals(ancestor, pipeline);
             ancestor = ancestor.Parent)
        {
            if (ancestor is ScriptBlockExpressionAst)
                return true;
        }

        return false;
    }

    internal static bool TryGetStreamCommand(
        CommandAst command,
        out PowerShellStreamCommandKind kind,
        out ExpressionAst message)
    {
        kind = default;
        message = null!;
        if (command.Parent is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count != 1 ||
            command.Redirections.Count != 0)
            return false;

        switch (command.GetCommandName()?.ToUpperInvariant())
        {
            case "WRITE-VERBOSE": kind = PowerShellStreamCommandKind.Verbose; break;
            case "WRITE-DEBUG": kind = PowerShellStreamCommandKind.Debug; break;
            case "WRITE-WARNING": kind = PowerShellStreamCommandKind.Warning; break;
            default: return false;
        }

        var arguments = command.CommandElements.Skip(1).ToArray();
        if (arguments.Length == 1 && arguments[0] is ExpressionAst positional)
        {
            message = positional;
            return true;
        }
        if (arguments.Length == 2 &&
            arguments[0] is CommandParameterAst parameter &&
            parameter.ParameterName.Equals("Message", StringComparison.OrdinalIgnoreCase) &&
            arguments[1] is ExpressionAst named)
        {
            message = named;
            return true;
        }
        return false;
    }
}
