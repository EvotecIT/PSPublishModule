using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Reads statically known module export declarations so compiled functions retain the original public surface.
/// </summary>
internal sealed class PowerShellModuleExportContract
{
    private PowerShellModuleExportContract(
        CommandAst[] commands,
        string[] functions,
        string[] cmdlets,
        string[] aliases,
        string[] variables,
        bool hasRuntimeControlledExports = false)
    {
        Commands = commands;
        Functions = functions;
        Cmdlets = cmdlets;
        Aliases = aliases;
        Variables = variables;
        HasRuntimeControlledExports = hasRuntimeControlledExports;
    }

    internal CommandAst[] Commands { get; }
    internal string[] Functions { get; }
    internal string[] Cmdlets { get; }
    internal string[] Aliases { get; }
    internal string[] Variables { get; }
    internal bool HasRuntimeControlledExports { get; }

    internal static PowerShellModuleExportContract? TryRead(ScriptBlockAst ast)
    {
        // A module-scope function can add exports when called by the initializer.
        if (ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<FunctionDefinitionAst>()
            .Any(static function => function.Body.Find(
                static node => node is CommandAst command && IsExportModuleMember(command),
                searchNestedScriptBlocks: false) is not null))
            return RuntimeControlledExports();

        var allCommands = ast.FindAll(
                static node => node is CommandAst command && IsExportModuleMember(command),
                searchNestedScriptBlocks: false)
            .Cast<CommandAst>()
            .ToArray();
        if (allCommands.Length == 0)
            return null;

        var statements = ast.EndBlock?.Statements;
        var commands = (statements?.AsEnumerable() ?? Enumerable.Empty<StatementAst>())
            .OfType<PipelineAst>()
            .Where(static pipeline => pipeline.PipelineElements.Count == 1)
            .Select(static pipeline => pipeline.PipelineElements[0])
            .OfType<CommandAst>()
            .Where(IsExportModuleMember)
            .OrderBy(static command => command.Extent.StartOffset)
            .ToArray();
        if (commands.Length != allCommands.Length)
            return RuntimeControlledExports();

        var usesRuntimeBinding = false;
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Function"] = new List<string>(),
            ["Cmdlet"] = new List<string>(),
            ["Alias"] = new List<string>(),
            ["Variable"] = new List<string>()
        };
        foreach (var command in commands)
        {
            var exportIndex = statements!.IndexOf((PipelineAst)command.Parent);
            if (IsAdjacentLiteralSplat(command, statements, exportIndex))
            {
                usesRuntimeBinding = true;
                continue;
            }

            var assignments = ReadAdjacentLiteralAssignments(statements, exportIndex);
            string? currentParameter = null;
            foreach (var element in command.CommandElements.Skip(1))
            {
                if (element is CommandParameterAst parameter)
                {
                    currentParameter = ResolveParameter(parameter);
                    if (parameter.Argument is not null)
                    {
                        values[currentParameter].AddRange(ReadExportValues(ast, currentParameter, parameter.Argument, assignments, ref usesRuntimeBinding));
                        currentParameter = null;
                    }
                    continue;
                }
                if (currentParameter is null)
                    throw new InvalidOperationException($"Export-ModuleMember at line {command.Extent.StartLineNumber} uses positional or dynamic export arguments; use literal -Function, -Cmdlet, -Alias, or -Variable values.");
                values[currentParameter].AddRange(ReadExportValues(ast, currentParameter, element, assignments, ref usesRuntimeBinding));
            }
        }

        // Existing variable attributes may coerce either a literal value or a splat.
        // Keep the authored export call so PowerShell binds the actual arguments.
        if (usesRuntimeBinding)
            return RuntimeControlledExports();

        return new PowerShellModuleExportContract(
            commands,
            Normalize(values["Function"]),
            Normalize(values["Cmdlet"]),
            Normalize(values["Alias"]),
            Normalize(values["Variable"]));
    }

    private static PowerShellModuleExportContract RuntimeControlledExports()
        => new(
            Array.Empty<CommandAst>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            hasRuntimeControlledExports: true);

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

    internal static bool IsExportModuleMember(CommandAst command)
        => command.GetCommandName() is { } name &&
           (name.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Microsoft.PowerShell.Core\\Export-ModuleMember", StringComparison.OrdinalIgnoreCase));

    private static string ResolveParameter(CommandParameterAst parameter)
    {
        foreach (var name in new[] { "Function", "Cmdlet", "Alias", "Variable" })
        {
            if (name.StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        throw new InvalidOperationException($"Export-ModuleMember parameter '-{parameter.ParameterName}' is not supported by binary-module compilation.");
    }

    private static bool IsAdjacentLiteralSplat(CommandAst command, IReadOnlyList<StatementAst> statements, int exportIndex)
    {
        if (exportIndex <= 0 || command.CommandElements.Count != 2 || command.Redirections.Count != 0 ||
            command.CommandElements[1] is not VariableExpressionAst { Splatted: true, VariablePath.IsUnqualified: true } splat ||
            statements[exportIndex - 1] is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Left: VariableExpressionAst { VariablePath.IsUnqualified: true } variable,
                Right: CommandExpressionAst { Redirections.Count: 0, Expression: HashtableAst options }
            } ||
            !variable.VariablePath.UserPath.Equals(splat.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            return false;

        return options.KeyValuePairs.All(pair =>
            pair.Item1 is StringConstantExpressionAst key &&
            (key.Value.Equals("Function", StringComparison.OrdinalIgnoreCase) ||
             key.Value.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase) ||
             key.Value.Equals("Alias", StringComparison.OrdinalIgnoreCase) ||
             key.Value.Equals("Variable", StringComparison.OrdinalIgnoreCase)) &&
            pair.Item2 is PipelineAst { PipelineElements: { Count: 1 } } pipeline &&
            pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } initializer &&
            TryReadLiteralValues(initializer.Expression) is not null);
    }

    private static HashSet<string> ReadAdjacentLiteralAssignments(
        IReadOnlyList<StatementAst> statements,
        int exportIndex)
    {
        var assignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = exportIndex - 1; index >= 0; index--)
        {
            // Stop at the first statement that could change the value or binding of a variable.
            if (statements[index] is not AssignmentStatementAst
                {
                    Operator: TokenKind.Equals,
                    Left: VariableExpressionAst { VariablePath.IsUnqualified: true } variable,
                    Right: CommandExpressionAst { Redirections.Count: 0 } initializer
                } ||
                TryReadLiteralValues(initializer.Expression) is null)
            {
                break;
            }

            assignments.Add(variable.VariablePath.UserPath);
        }

        return assignments;
    }

    private static IReadOnlyList<string> ReadExportValues(
        ScriptBlockAst root,
        string parameterName,
        CommandElementAst element,
        HashSet<string> assignments,
        ref bool usesRuntimeBinding)
    {
        if (element is VariableExpressionAst { Splatted: false, VariablePath.IsUnqualified: true } variable &&
            assignments.Contains(variable.VariablePath.UserPath) ||
            parameterName == "Function" && PowerShellConventionalModuleSourceDiscovery.IsFileNameExport(root, element))
        {
            usesRuntimeBinding = true;
            return Array.Empty<string>();
        }

        return TryReadLiteralValues(element) ??
            throw new InvalidOperationException($"Export-ModuleMember at line {element.Extent.StartLineNumber} contains a non-literal export expression.");
    }

    private static IReadOnlyList<string>? TryReadLiteralValues(CommandElementAst element)
    {
        if (element is StringConstantExpressionAst text && !string.IsNullOrWhiteSpace(text.Value))
            return new[] { text.Value };
        if (element is ArrayLiteralAst array)
        {
            var values = new List<string>();
            foreach (var item in array.Elements)
            {
                if (item is not StringConstantExpressionAst literal || string.IsNullOrWhiteSpace(literal.Value))
                    return null;
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
                    pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0 } commandExpression ||
                    TryReadLiteralValues(commandExpression.Expression) is not { } statementValues)
                {
                    return null;
                }
                values.AddRange(statementValues);
            }
            return values;
        }
        return null;
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
