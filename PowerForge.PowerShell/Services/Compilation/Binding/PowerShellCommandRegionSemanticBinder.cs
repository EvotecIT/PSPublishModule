using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCommandRegionSemanticBinder
{
    internal static PowerShellBoundCommandRegionStatement BindRegion(
        ParsedSourceDocument document,
        IReadOnlyList<StatementAst> statements,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        PowerShellCommandSemanticRegistry commandRegistry)
    {
        var arguments = BindArguments(statements, symbols, parameters);
        var source = CreateParameterBlock(arguments) + Environment.NewLine +
                     string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
        var span = PowerShellSourceParser.GetSpan(document, statements[0].Extent);
        if (statements.Count > 1)
        {
            var last = PowerShellSourceParser.GetSpan(document, statements[statements.Count - 1].Extent);
            span = new SourceSpan(span.DocumentId, span.StartOffset, last.EndOffset, span.StartLine, span.StartColumn, last.EndLine, last.EndColumn);
        }
        return new PowerShellBoundCommandRegionStatement(span, source, arguments, BindStages(document, statements, commandRegistry), statements.Count);
    }

    internal static PowerShellBoundCommandCaptureStatement BindCapture(
        ParsedSourceDocument document,
        AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        PowerShellCommandSemanticRegistry commandRegistry)
    {
        var targetSyntax = (VariableExpressionAst)((ConvertExpressionAst)assignment.Left).Child;
        var target = symbols[targetSyntax.VariablePath.UserPath];
        var referenced = new Ast[] { assignment.Right };
        var arguments = BindArguments(referenced, symbols, parameters);
        var source = CreateParameterBlock(arguments) + Environment.NewLine + assignment.Right.Extent.Text;
        return new PowerShellBoundCommandCaptureStatement(
            PowerShellSourceParser.GetSpan(document, assignment.Extent),
            target.Symbol,
            ((ConvertExpressionAst)assignment.Left).StaticType,
            source,
            arguments,
            BindStages(document, referenced, commandRegistry));
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

    private static string CreateParameterBlock(IEnumerable<PowerShellBoundCommandRegionArgument> arguments)
        => "param(" + string.Join(", ", arguments.Select(argument =>
            (argument.IsSwitch ? "[switch] " : string.Empty) + EmitBracedVariable(argument.Symbol.Name))) + ")";

    private static string EmitBracedVariable(string name)
        => "${" + name.Replace("`", "``").Replace("}", "`}") + "}";

    private static PowerShellBoundCommandStage[] BindStages<TAst>(
        ParsedSourceDocument document,
        IEnumerable<TAst> syntax,
        PowerShellCommandSemanticRegistry commandRegistry)
        where TAst : Ast
        => syntax.SelectMany(static item => item.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true))
            .Cast<CommandAst>()
            .OrderBy(static command => command.Extent.StartOffset)
            .Select(command =>
            {
                return PowerShellCommandStageSemanticBinder.Bind(document, command, commandRegistry);
            })
            .ToArray();
}
