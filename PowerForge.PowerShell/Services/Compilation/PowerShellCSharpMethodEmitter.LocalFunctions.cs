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
            arguments.AddRange(new[] { "__invokePowerShellRegion", "__invokePowerShellCapture" });
        if (call.Signature.RequiresPowerShellRuntimeState)
            arguments.AddRange(new[] { "__shouldProcessTarget", "__shouldProcessAction", "__psVersion", "__whatIfPreference" });
        if (call.Signature.RequiresPowerShellBoundParameters)
            arguments.Add(EmitBoundParameterSet(call.BoundParameterNames));
        var invocation = $"{call.Signature.GeneratedName}({string.Join(", ", arguments)})";
        if (call.ArgumentEvaluations.Length == 0)
            return invocation;

        var evaluations = string.Join(" ", call.ArgumentEvaluations);
        return call.Signature.ReturnType == typeof(void)
            ? $"(new global::System.Action(() => {{ {evaluations} {invocation}; }}))()"
            : $"(new global::System.Func<{GetTypeName(call.Signature.ReturnType)}>(() => {{ {evaluations} return {invocation}; }}))()";
    }

    private BoundLocalFunctionCall BindLocalFunctionCall(CommandAst command)
    {
        if (command.Redirections.Count != 0)
            throw Error(command, "Typed local function calls do not support stream redirection.");
        var name = command.GetCommandName();
        if (name is null || !_localFunctions.TryGetValue(name, out var signature))
            throw Error(command, $"Command '{name ?? command.Extent.Text}' is not a statically known local function.");
        if (signature.RequiresPowerShellShouldProcess)
            throw Error(command, $"Local function '{signature.SourceName}' uses ShouldProcess and must remain on the PowerShell command path so its command identity and ConfirmImpact are preserved.");
        if (signature.RequiresPowerShellCommandRegions && !IsDirectLocalFunctionOutput(command))
            throw Error(command, $"Local function '{signature.SourceName}' emits PowerShell command-region success output whose pipeline cardinality cannot be preserved when the call result is consumed.");
        if (signature.ReturnType.IsArray && !IsDirectLocalFunctionOutput(command))
            throw Error(command, $"Local function '{signature.SourceName}' returns an array whose PowerShell pipeline cardinality cannot be preserved when the result is consumed directly.");
        if (signature.Parameters.SelectMany(static parameter => parameter.Bindings)
            .Any(static binding => !string.IsNullOrWhiteSpace(binding.ParameterSetName)))
            throw Error(command, $"Local function '{signature.SourceName}' uses named parameter sets whose selection must remain on the PowerShell binding path.");
        if (!_capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) &&
            signature.Parameters.Any(static parameter => parameter.Validations.Length > 0) &&
            IsInsideTypeDiscriminatingTry(command))
        {
            throw Error(
                command,
                $"Local function '{signature.SourceName}' performs parameter validation inside a typed try/catch, whose PowerShell binding-exception identity cannot be preserved without a PowerShell host.");
        }

        var bound = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var authoredArguments = new List<AuthoredLocalFunctionArgument>();
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
                var namedValue = BindArgument(parameter, argument);
                bound[parameter.Name] = namedValue;
                authoredArguments.Add(new AuthoredLocalFunctionArgument(parameter, namedValue));
                continue;
            }

            if (elements[index] is not ExpressionAst positional)
                throw Error(elements[index], "Typed local function calls accept only scalar named or positional arguments; splatting and dynamic command elements require PowerShell.");
            if (!signature.CommandBinding.PositionalBinding ||
                signature.Parameters.SelectMany(static parameter => parameter.Bindings).Any(static binding => binding.Position.HasValue))
                throw Error(positional, $"Local function '{signature.SourceName}' uses an explicit positional-binding contract that is not represented by source-order typed calls.");
            while (positionalIndex < signature.Parameters.Length && bound.ContainsKey(signature.Parameters[positionalIndex].Name))
                positionalIndex++;
            if (positionalIndex >= signature.Parameters.Length)
                throw Error(positional, $"Local function '{signature.SourceName}' received more positional arguments than its typed parameter contract permits.");
            var positionalParameter = signature.Parameters[positionalIndex++];
            var positionalValue = BindArgument(positionalParameter, positional);
            bound[positionalParameter.Name] = positionalValue;
            authoredArguments.Add(new AuthoredLocalFunctionArgument(positionalParameter, positionalValue));
        }

        var declarationOrder = signature.Parameters
            .Where(parameter => authoredArguments.Any(argument =>
                argument.Parameter.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(static parameter => parameter.Name);
        var authoredOrder = authoredArguments.Select(static argument => argument.Parameter.Name);
        var argumentEvaluations = new List<string>();
        if (!authoredOrder.SequenceEqual(declarationOrder, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var argument in authoredArguments)
            {
                var temporary = GetTemporaryIdentifier("local_argument");
                argumentEvaluations.Add($"{GetTypeName(argument.Parameter.Type)} {temporary} = {argument.Value};");
                bound[argument.Parameter.Name] = temporary;
            }
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
        return new BoundLocalFunctionCall(
            signature,
            arguments.ToArray(),
            bound.Keys.ToArray(),
            argumentEvaluations.ToArray());
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
        return parameter.Type == typeof(string) && !parameter.AllowNull ? $"({source} ?? string.Empty)" : source;
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
        var commonParameters = PowerShellCommonParameterPolicy.GetStandard(signature.IsAdvancedFunction, _targetFramework);
        var commonExact = commonParameters.Count(parameter =>
            parameter.Name.Equals(argument.ParameterName, StringComparison.OrdinalIgnoreCase) ||
            parameter.Alias.Equals(argument.ParameterName, StringComparison.OrdinalIgnoreCase));
        if (commonExact > 0)
            throw Error(argument, $"Typed local function calls do not support advanced-function common parameter '-{argument.ParameterName}'.");
        var commonAbbreviations = commonParameters.Count(parameter =>
            parameter.Name.StartsWith(argument.ParameterName, StringComparison.OrdinalIgnoreCase) ||
            parameter.Alias.StartsWith(argument.ParameterName, StringComparison.OrdinalIgnoreCase));
        return (abbreviated.Length, commonAbbreviations) switch
        {
            (1, 0) => abbreviated[0],
            (0, 0) => throw Error(argument, $"Local function '{signature.SourceName}' has no parameter matching '-{argument.ParameterName}'."),
            (0, 1) => throw Error(argument, $"Typed local function calls do not support advanced-function common parameter abbreviation '-{argument.ParameterName}'."),
            _ => throw Error(argument, $"Local function parameter abbreviation '-{argument.ParameterName}' is ambiguous.")
        };
    }

    private static CommandAst GetLocalCommand(PipelineAst pipeline)
        => pipeline.PipelineElements[0] as CommandAst
           ?? throw new InvalidOperationException("A local function pipeline must contain one command.");

    private static bool IsDirectLocalFunctionOutput(CommandAst command)
    {
        Ast current = command;
        while (current.Parent is PipelineAst or CommandExpressionAst or ParenExpressionAst)
            current = current.Parent;
        return current.Parent is ReturnStatementAst ||
               current is PipelineAst pipeline && pipeline.Parent is NamedBlockAst namedBlock &&
               ReferenceEquals(namedBlock.Statements.LastOrDefault(), pipeline);
    }

    private static bool IsInsideTypeDiscriminatingTry(CommandAst command)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (current is not TryStatementAst tryStatement ||
                tryStatement.CatchClauses.All(static clause => clause.CatchTypes.Count == 0))
                continue;
            if (command.Extent.StartOffset >= tryStatement.Body.Extent.StartOffset &&
                command.Extent.EndOffset <= tryStatement.Body.Extent.EndOffset)
                return true;
        }
        return false;
    }

    private sealed class BoundLocalFunctionCall
    {
        internal BoundLocalFunctionCall(
            PowerShellLocalFunctionSignature signature,
            string[] arguments,
            string[] boundParameterNames,
            string[] argumentEvaluations)
        {
            Signature = signature;
            Arguments = arguments;
            BoundParameterNames = boundParameterNames;
            ArgumentEvaluations = argumentEvaluations;
        }
        internal PowerShellLocalFunctionSignature Signature { get; }
        internal string[] Arguments { get; }
        internal string[] BoundParameterNames { get; }
        internal string[] ArgumentEvaluations { get; }
    }

    private sealed class AuthoredLocalFunctionArgument
    {
        internal AuthoredLocalFunctionArgument(PowerShellLocalFunctionParameter parameter, string value)
        {
            Parameter = parameter;
            Value = value;
        }

        internal PowerShellLocalFunctionParameter Parameter { get; }
        internal string Value { get; }
    }
}
