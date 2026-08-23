using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Reads literal module export declarations so compiled functions retain the original public surface.
/// </summary>
internal sealed class PowerShellModuleExportContract
{
    private PowerShellModuleExportContract(
        CommandAst[] commands,
        string[] functions,
        string[] cmdlets,
        string[] aliases,
        string[] variables)
    {
        Commands = commands;
        Functions = functions;
        Cmdlets = cmdlets;
        Aliases = aliases;
        Variables = variables;
    }

    internal CommandAst[] Commands { get; }
    internal string[] Functions { get; }
    internal string[] Cmdlets { get; }
    internal string[] Aliases { get; }
    internal string[] Variables { get; }

    internal static PowerShellModuleExportContract? TryRead(ScriptBlockAst ast)
    {
        var commands = ast.FindAll(
                static node => node is CommandAst command &&
                               command.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true,
                searchNestedScriptBlocks: false)
            .Cast<CommandAst>()
            .OrderBy(static command => command.Extent.StartOffset)
            .ToArray();
        if (commands.Length == 0)
            return null;

        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Function"] = new List<string>(),
            ["Cmdlet"] = new List<string>(),
            ["Alias"] = new List<string>(),
            ["Variable"] = new List<string>()
        };
        foreach (var command in commands)
        {
            string? currentParameter = null;
            foreach (var element in command.CommandElements.Skip(1))
            {
                if (element is CommandParameterAst parameter)
                {
                    currentParameter = ResolveParameter(parameter);
                    continue;
                }
                if (currentParameter is null)
                    throw new InvalidOperationException($"Export-ModuleMember at line {command.Extent.StartLineNumber} uses positional or dynamic export arguments; use literal -Function, -Cmdlet, -Alias, or -Variable values.");
                foreach (var value in ReadLiteralValues(element))
                    values[currentParameter].Add(value);
            }
        }

        return new PowerShellModuleExportContract(
            commands,
            Normalize(values["Function"]),
            Normalize(values["Cmdlet"]),
            Normalize(values["Alias"]),
            Normalize(values["Variable"]));
    }

    internal string[] SelectFunctions(IEnumerable<string> names)
        => Select(names, Functions);

    internal static PowerShellModuleExportContract? TryRead(string sourcePath)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Module export declarations could not be parsed.");
        return TryRead(ast);
    }

    private static string ResolveParameter(CommandParameterAst parameter)
    {
        foreach (var name in new[] { "Function", "Cmdlet", "Alias", "Variable" })
        {
            if (name.StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        throw new InvalidOperationException($"Export-ModuleMember parameter '-{parameter.ParameterName}' is not supported by binary-module compilation.");
    }

    private static IEnumerable<string> ReadLiteralValues(CommandElementAst element)
    {
        if (element is StringConstantExpressionAst text && !string.IsNullOrWhiteSpace(text.Value))
            return new[] { text.Value };
        if (element is ArrayLiteralAst array)
        {
            var values = new List<string>();
            foreach (var item in array.Elements)
            {
                if (item is not StringConstantExpressionAst literal || string.IsNullOrWhiteSpace(literal.Value))
                    throw new InvalidOperationException($"Export-ModuleMember at line {element.Extent.StartLineNumber} contains a non-literal export name.");
                values.Add(literal.Value);
            }
            return values;
        }
        if (element is ArrayExpressionAst arrayExpression)
        {
            var values = new List<string>();
            foreach (var statement in arrayExpression.SubExpression.Statements)
            {
                if (statement is not PipelineAst { PipelineElements: { Count: 1 } } pipeline ||
                    pipeline.PipelineElements[0] is not CommandExpressionAst commandExpression)
                {
                    throw new InvalidOperationException($"Export-ModuleMember at line {element.Extent.StartLineNumber} contains a non-literal export array.");
                }
                values.AddRange(ReadLiteralValues(commandExpression.Expression));
            }
            return values;
        }
        throw new InvalidOperationException($"Export-ModuleMember at line {element.Extent.StartLineNumber} contains a non-literal export expression.");
    }

    private static string[] Select(IEnumerable<string> names, string[] patterns)
    {
        var matchers = patterns.Select(pattern => new WildcardPattern(pattern, WildcardOptions.IgnoreCase)).ToArray();
        return names
            .Where(name => matchers.Any(matcher => matcher.IsMatch(name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] Normalize(IEnumerable<string> values)
        => values.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
