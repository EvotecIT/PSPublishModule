using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCommandRegionSemanticBinder
{
    internal static bool IsBackground(PipelineAst pipeline)
        => pipeline.GetType().GetProperty("Background")?.GetValue(pipeline) is true;

    internal static bool HasPipelineOperators(PipelineAst pipeline)
        => pipeline.PipelineElements.Any(command => command.Redirections.Count > 0) ||
           IsBackground(pipeline);

    internal static bool RequiresPipelineSyntax(PipelineAst pipeline)
        => pipeline.PipelineElements.Count != 1 || pipeline.PipelineElements[0] is CommandAst || HasPipelineOperators(pipeline);

    /// <summary>One ordinary hosted command can supply an authored literal value through the existing capture boundary.</summary>
    internal static bool IsNativeLiteralCommandValue(StatementAst statement, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
           capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) &&
           statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           !HasPipelineOperators(pipeline) &&
           pipeline.PipelineElements[0] is CommandAst { InvocationOperator: TokenKind.Unknown } command &&
           command.GetCommandName() is not null;

    /// <summary>
    /// Identifies a complete authored pipeline whose command binding, stream routing, script-block state,
    /// and retained mutations must remain owned by the active PowerShell invocation.
    /// </summary>
    internal static bool RequiresNativePipelineBinding(PipelineAst pipeline)
        => pipeline.PipelineElements.Count > 1 &&
           pipeline.PipelineElements.Any(static element => element is CommandAst) &&
           !BeginsWithTypedInvocationCandidate(pipeline) &&
           !TerminatesInExplicitSuccessStreamSink(pipeline);

    /// <summary>
    /// Preserves the established typed-continuation candidate when an invocation supplies a downstream
    /// hosted pipeline. Transparent parentheses do not change that ownership decision. The semantic binder
    /// still decides whether the invocation is actually compilable; otherwise the path remains fail-closed.
    /// </summary>
    private static bool BeginsWithTypedInvocationCandidate(PipelineAst pipeline)
    {
        Ast syntax = pipeline.PipelineElements[0];
        while (true)
        {
            switch (syntax)
            {
                case CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case ParenExpressionAst { Pipeline: PipelineAst { PipelineElements.Count: 1 } innerPipeline }:
                    syntax = innerPipeline.PipelineElements[0];
                    continue;
                default:
                    return syntax is InvokeMemberExpressionAst;
            }
        }
    }

    /// <summary>
    /// Keeps explicit success-stream suppression under its existing fail-closed contract instead of
    /// treating the sink as an ordinary output-producing pipeline stage.
    /// </summary>
    private static bool TerminatesInExplicitSuccessStreamSink(PipelineAst pipeline)
    {
        if (pipeline.PipelineElements.LastOrDefault() is not CommandAst command ||
            command.GetCommandName() is not { } commandName) return false;
        var separator = commandName.LastIndexOf('\\');
        var leafName = separator < 0 ? commandName : commandName.Substring(separator + 1);
        return string.Equals(leafName, "Out-Null", StringComparison.OrdinalIgnoreCase);
    }

    internal static PowerShellBoundNativeCommandExpression BindNativeCapture(ParsedSourceDocument document, Ast syntax,
        Ast authoredSyntax, PowerShellCommandSemanticResolver commandResolver, ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
    {
        ParenExpressionAst? wrapper = null;
        for (var node = syntax; node is not null; node = node.Parent)
        {
            if (node is ParenExpressionAst parenthesized) { wrapper = parenthesized; break; }
            if (ReferenceEquals(node, authoredSyntax)) break;
        }
        var preserve = wrapper is not null && (bool)(typeof(ExpressionAst).GetMethod(
            "ShouldPreserveOutputInCaseOfException", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(wrapper, null)!);
        return new PowerShellBoundNativeCommandExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
            syntax.Extent.Text, document.Path, document.Text, preserve,
            BindStages(document, new[] { syntax }, commandResolver, localFunctionNames, capabilities));
    }

    internal static bool TryBindNativePipeline(ParsedSourceDocument document, StatementAst statement,
        PowerShellCommandSemanticResolver commandResolver, ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities, out PowerShellBoundCommandRegionStatement? region)
    {
        region = null;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) ||
            statement is not PipelineAst pipeline) return false;
        var commands = pipeline.PipelineElements.OfType<CommandAst>().ToArray();
        var hasPipelineOperator = HasPipelineOperators(pipeline);
        if (!hasPipelineOperator && !RequiresNativePipelineBinding(pipeline) && (commands.Length == 0 || commands.All(command =>
                commandResolver.IsRuntimeFreeCompilerIntrinsic(command, localFunctionNames, capabilities)))) return false;
        region = new PowerShellBoundCommandRegionStatement(PowerShellSourceParser.GetSpan(document, pipeline.Extent),
            pipeline.Extent.Text, Array.Empty<PowerShellBoundCommandRegionArgument>(),
            BindStages(document, new[] { pipeline }, commandResolver, localFunctionNames, capabilities),
            nativeSourcePath: document.Path, nativeSourceDocument: document.Text);
        return true;
    }

    internal static PowerShellBoundCommandRegionStatement BindRegion(
        ParsedSourceDocument document,
        IReadOnlyList<StatementAst> statements,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        PowerShellCommandSemanticResolver commandResolver,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
    {
        var arguments = BindArguments(statements, symbols, parameters);
        var source = CreateParameterBlock(arguments, statements) + Environment.NewLine +
                     string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
        var span = PowerShellSourceParser.GetSpan(document, statements[0].Extent);
        if (statements.Count > 1)
        {
            var last = PowerShellSourceParser.GetSpan(document, statements[statements.Count - 1].Extent);
            span = new SourceSpan(span.DocumentId, span.StartOffset, last.EndOffset, span.StartLine, span.StartColumn, last.EndLine, last.EndColumn);
        }
        return new PowerShellBoundCommandRegionStatement(
            span,
            source,
            arguments,
            BindStages(document, statements, commandResolver, localFunctionNames, capabilities),
            statements.Count, sourceSelection: GetSourceSelection(document, statements.Select(static statement => statement.Extent)));
    }

    internal static PowerShellBoundCommandCaptureStatement BindCapture(
        ParsedSourceDocument document,
        AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        PowerShellCommandSemanticResolver commandResolver,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
    {
        var targetSyntax = (VariableExpressionAst)((ConvertExpressionAst)assignment.Left).Child;
        var target = symbols[targetSyntax.VariablePath.UserPath];
        var referenced = new Ast[] { assignment.Right };
        var arguments = BindArguments(referenced, symbols, parameters);
        var source = CreateParameterBlock(arguments, referenced) + Environment.NewLine + assignment.Right.Extent.Text;
        return new PowerShellBoundCommandCaptureStatement(
            PowerShellSourceParser.GetSpan(document, assignment.Extent),
            target.Symbol,
            ((ConvertExpressionAst)assignment.Left).StaticType,
            source,
            arguments,
            BindStages(document, referenced, commandResolver, localFunctionNames, capabilities),
            GetSourceSelection(document, new[] { assignment.Right.Extent }));
    }

    private static PowerShellCommandRegionSourceSelection GetSourceSelection(
        ParsedSourceDocument document,
        IEnumerable<IScriptExtent> extents)
        => document.AuthoredProjection?.Select(document, extents) ??
           new PowerShellCommandRegionSourceSelection(document.Path, document.Text,
               extents.Select(extent => PowerShellSourceParser.GetSpan(document, extent)));

    private static PowerShellBoundCommandRegionArgument[] BindArguments<TAst>(
        IEnumerable<TAst> syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters)
        where TAst : Ast
        => syntax.SelectMany(static item => item.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true))
            .Cast<VariableExpressionAst>()
            .Select(static variable => variable.VariablePath.UserPath)
            .Where(symbols.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new PowerShellBoundCommandRegionArgument(symbols[name].Symbol, parameters.TryGetValue(name, out var parameter) && parameter.Contract.IsSwitch))
            .ToArray();

    private static string CreateParameterBlock(
        IEnumerable<PowerShellBoundCommandRegionArgument> arguments,
        IEnumerable<Ast> syntax)
    {
        var materialized = arguments.ToArray();
        var reservedNames = syntax
            .SelectMany(static item => item.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true))
            .Cast<VariableExpressionAst>()
            .Select(static variable => variable.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parameters = new string[materialized.Length];
        var switchInitializers = new List<string>();
        for (var index = 0; index < materialized.Length; index++)
        {
            var argument = materialized[index];
            if (!argument.IsSwitch)
            {
                parameters[index] = EmitBracedVariable(argument.Symbol.Name);
                continue;
            }

            var temporaryName = "__PowerForgeSwitchArgument" + index;
            while (!reservedNames.Add(temporaryName)) temporaryName += "_";
            var temporary = EmitBracedVariable(temporaryName);
            parameters[index] = "[bool] " + temporary;
            switchInitializers.Add(
                $"{EmitBracedVariable(argument.Symbol.Name)} = [System.Management.Automation.SwitchParameter]::new([bool]{temporary})");
        }

        var parameterBlock = "param(" + string.Join(", ", parameters) + ")";
        return switchInitializers.Count == 0
            ? parameterBlock
            : parameterBlock + Environment.NewLine + string.Join(Environment.NewLine, switchInitializers);
    }

    private static string EmitBracedVariable(string name)
        => "${" + name.Replace("`", "``").Replace("}", "`}") + "}";

    private static PowerShellBoundCommandStage[] BindStages<TAst>(
        ParsedSourceDocument document,
        IEnumerable<TAst> syntax,
        PowerShellCommandSemanticResolver commandResolver,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
        where TAst : Ast
        => syntax.SelectMany(static item => item.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true))
            .Cast<CommandAst>()
            .OrderBy(static command => command.Extent.StartOffset)
            .Select(command =>
            {
                return PowerShellCommandStageSemanticBinder.Bind(document, command, commandResolver, localFunctionNames, capabilities);
            })
            .ToArray();
}
