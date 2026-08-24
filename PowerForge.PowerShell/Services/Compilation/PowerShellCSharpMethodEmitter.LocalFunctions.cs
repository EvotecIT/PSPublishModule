using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private bool IsLocalFunctionPipeline(PipelineAst pipeline)
        => pipeline.PipelineElements.Count == 1 &&
           pipeline.PipelineElements[0] is CommandAst command &&
           IsLocalFunctionCommand(command);

    private bool IsLocalFunctionCommand(CommandAst command)
        => _capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls) &&
           command.InvocationOperator == TokenKind.Unknown &&
           command.GetCommandName() is { } name &&
           _localFunctions.ContainsKey(name);

    private PowerShellLocalFunctionSignature[] GetCalledLocalFunctions(IEnumerable<StatementAst> statements)
        => statements
            .SelectMany(static statement => statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false))
            .OfType<CommandAst>()
            .Select(command => command.GetCommandName())
            .Where(static name => name is not null)
            .Select(name => _localFunctions.TryGetValue(name!, out var signature) ? signature : null)
            .Where(static signature => signature is not null)
            .Cast<PowerShellLocalFunctionSignature>()
            .ToArray();

    private Type InferLocalFunctionType(PipelineAst pipeline)
        => BindLocalFunctionCall(GetLocalCommand(pipeline)).Signature.ReturnType;

    private Type InferLocalFunctionType(CommandAst command)
        => BindLocalFunctionCall(command).Signature.ReturnType;

    private string EmitLocalFunctionCall(PipelineAst pipeline)
        => EmitLocalFunctionCall(GetLocalCommand(pipeline));

    private string EmitLocalFunctionCall(CommandAst command)
    {
        var call = BindLocalFunctionCall(command);
        var arguments = call.Arguments.ToList();
        if (call.Signature.RequiresPowerShellStreams)
            arguments.AddRange(new[] { "__writeVerbose", "__writeDebug", "__writeWarning" });
        if (call.Signature.RequiresPowerShellCommandRegions)
            arguments.Add("__invokePowerShellRegion");
        if (call.Signature.RequiresPowerShellBoundParameters)
            arguments.Add(EmitBoundParameterSet(call.BoundParameterNames));
        return $"{call.Signature.GeneratedName}({string.Join(", ", arguments)})";
    }

    private BoundLocalFunctionCall BindLocalFunctionCall(CommandAst command)
    {
        if (command.Redirections.Count != 0)
            throw Error(command, "Typed local function calls do not support stream redirection.");
        var name = command.GetCommandName();
        if (name is null || !_localFunctions.TryGetValue(name, out var signature))
            throw Error(command, $"Command '{name ?? command.Extent.Text}' is not a statically known local function.");

        var bound = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionalIndex = 0;
        var elements = command.CommandElements.Skip(1).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is CommandParameterAst named)
            {
                var parameter = ResolveParameter(signature, named);
                if (bound.ContainsKey(parameter.Name))
                    throw Error(named, $"Local function parameter '-{parameter.Name}' is bound more than once.");
                if (parameter.IsSwitch && named.Argument is null)
                {
                    bound[parameter.Name] = "true";
                    continue;
                }
                Ast argument;
                if (named.Argument is not null)
                    argument = named.Argument;
                else if (index + 1 < elements.Length && elements[index + 1] is ExpressionAst expression)
                {
                    argument = expression;
                    index++;
                }
                else
                {
                    throw Error(named, $"Local function parameter '-{parameter.Name}' requires a statically typed argument value.");
                }
                bound[parameter.Name] = BindArgument(parameter, argument);
                continue;
            }

            if (elements[index] is not ExpressionAst positional)
                throw Error(elements[index], "Typed local function calls accept only scalar named or positional arguments; splatting and dynamic command elements require PowerShell.");
            while (positionalIndex < signature.Parameters.Length && bound.ContainsKey(signature.Parameters[positionalIndex].Name))
                positionalIndex++;
            if (positionalIndex >= signature.Parameters.Length)
                throw Error(positional, $"Local function '{signature.SourceName}' received more positional arguments than its typed parameter contract permits.");
            var positionalParameter = signature.Parameters[positionalIndex++];
            bound[positionalParameter.Name] = BindArgument(positionalParameter, positional);
        }

        var arguments = signature.Parameters.Select(parameter =>
        {
            if (bound.TryGetValue(parameter.Name, out var value))
                return value;
            if (parameter.IsMandatory)
                throw Error(command, $"Mandatory local function parameter '-{parameter.Name}' was not supplied.");
            if (parameter.Type == typeof(string)) return "string.Empty";
            if (parameter.Type == typeof(bool)) return "false";
            return $"default({GetTypeName(parameter.Type)})";
        }).ToList();
        return new BoundLocalFunctionCall(signature, arguments.ToArray(), bound.Keys.ToArray());
    }

    private static string EmitBoundParameterSet(IEnumerable<string> boundParameterNames)
    {
        var names = boundParameterNames
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(PowerShellCSharpLiteral.QuoteString);
        return "new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.OrdinalIgnoreCase) { " +
               string.Join(", ", names) + " }";
    }

    private string BindArgument(PowerShellLocalFunctionParameter parameter, Ast argument)
    {
        var sourceType = InferExpressionType(argument);
        if (!CanAssign(parameter.Type, sourceType) && !(IsNullExpression(argument) && !parameter.Type.IsValueType))
            throw Error(argument, $"Argument for local function parameter '-{parameter.Name}' has CLR type '{sourceType.FullName}', which is not assignable to '{parameter.Type.FullName}' without PowerShell conversion.");
        var source = EmitExpression(argument);
        return parameter.Type == typeof(string) ? $"({source} ?? string.Empty)" : source;
    }

    private PowerShellLocalFunctionParameter ResolveParameter(PowerShellLocalFunctionSignature signature, CommandParameterAst argument)
    {
        var exact = signature.Parameters.Where(parameter =>
            parameter.Name.Equals(argument.ParameterName, StringComparison.OrdinalIgnoreCase) ||
            parameter.Aliases.Any(alias => alias.Equals(argument.ParameterName, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (exact.Length == 1) return exact[0];
        var abbreviated = signature.Parameters.Where(parameter =>
            parameter.Name.StartsWith(argument.ParameterName, StringComparison.OrdinalIgnoreCase) ||
            parameter.Aliases.Any(alias => alias.StartsWith(argument.ParameterName, StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
        return abbreviated.Length switch
        {
            1 => abbreviated[0],
            0 => throw Error(argument, $"Local function '{signature.SourceName}' has no parameter matching '-{argument.ParameterName}'."),
            _ => throw Error(argument, $"Local function parameter abbreviation '-{argument.ParameterName}' is ambiguous.")
        };
    }

    private static CommandAst GetLocalCommand(PipelineAst pipeline)
        => pipeline.PipelineElements[0] as CommandAst
           ?? throw new InvalidOperationException("A local function pipeline must contain one command.");

    private sealed class BoundLocalFunctionCall
    {
        internal BoundLocalFunctionCall(PowerShellLocalFunctionSignature signature, string[] arguments, string[] boundParameterNames)
        { Signature = signature; Arguments = arguments; BoundParameterNames = boundParameterNames; }
        internal PowerShellLocalFunctionSignature Signature { get; }
        internal string[] Arguments { get; }
        internal string[] BoundParameterNames { get; }
    }
}
