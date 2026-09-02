using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCommandRegionSemanticBinder
{
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
            statements.Count);
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
            BindStages(document, referenced, commandResolver, localFunctionNames, capabilities));
    }

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
