using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellCompilationSourcePathPlanner
{
    /// <summary>Finds direct module-scope dot sources that may need a runtime path guard.</summary>
    internal static IEnumerable<CommandAst> GetModuleScopeDotSources(ScriptBlockAst root)
        => (root.EndBlock?.Statements.AsEnumerable() ?? Enumerable.Empty<StatementAst>())
            .OfType<PipelineAst>()
            .Where(static pipeline => pipeline.PipelineElements.Count == 1)
            .Select(static pipeline => pipeline.PipelineElements[0])
            .OfType<CommandAst>()
            .Where(static command => command.InvocationOperator == TokenKind.Dot);

    /// <summary>Recognizes a finite set of authored paths without executing the switch or resolving Join-Path.</summary>
    internal static bool TryPlanModuleAlternatives(
        ScriptBlockAst root, CommandAst command, out CommandElementAst? operand, out string[] paths)
    {
        operand = null;
        paths = Array.Empty<string>();
        if (command is not { InvocationOperator: TokenKind.Dot, Parent: PipelineAst { PipelineElements.Count: 1 } pipeline } ||
            root.EndBlock is null || !ReferenceEquals(pipeline.Parent, root.EndBlock) ||
            command.CommandElements.FirstOrDefault() is not ParenExpressionAst
            {
                Pipeline: PipelineAst { PipelineElements.Count: 1 } join
            } parenthesized ||
            join.PipelineElements[0] is not CommandAst { CommandElements.Count: 3, Redirections.Count: 0 } call ||
            call.InvocationOperator is TokenKind.Dot or TokenKind.Ampersand ||
            !IsJoinPathCommand(call) ||
            call.CommandElements[1] is not VariableExpressionAst scriptRoot || !IsScriptRoot(scriptRoot) ||
            call.CommandElements[2] is not VariableExpressionAst { VariablePath.IsUnqualified: true, Splatted: false } choice)
            return false;

        var name = choice.VariablePath.UserPath;
        var producer = root.EndBlock.Statements.OfType<AssignmentStatementAst>()
            .LastOrDefault(assignment => assignment.Extent.EndOffset <= command.Extent.StartOffset &&
                assignment.Left is VariableExpressionAst { VariablePath.IsUnqualified: true } target &&
                SameVariable(target, name));
        if (producer is not { Operator: TokenKind.Equals, Right: SwitchStatementAst { Flags: SwitchFlags.None, Default: { } defaultClause } selection } ||
            HasInterveningWrite(root, name, producer.Extent.EndOffset, command.Extent.StartOffset))
            return false;

        var alternatives = selection.Clauses.Select(static clause => clause.Item2)
            .Append(defaultClause).Select(ReadSwitchPath).ToArray();
        if (alternatives.Length < 2 || alternatives.Any(static path => path is null))
            return false;

        operand = parenthesized;
        paths = alternatives.Select(static path => path!).Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static string? ReadSwitchPath(StatementBlockAst clause)
    {
        if (clause.Traps?.Count > 0 || clause.Statements.Count != 1 ||
            clause.Statements[0] is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst
            {
                Redirections.Count: 0, Expression: StringConstantExpressionAst literal
            })
            return null;

        var path = literal.Value.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(path) &&
               path.IndexOfAny(new[] { ':', '\0' }) < 0 &&
               path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) &&
               !WildcardPattern.ContainsWildcardCharacters(path) &&
               path.Split('/').All(static segment => segment.Length != 0 && segment is not "." and not "..")
            ? path
            : null;
    }

    private static bool IsJoinPathCommand(CommandAst command)
    {
        var name = command.GetCommandName();
        return string.Equals(name, "Join-Path", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Microsoft.PowerShell.Management\\Join-Path", StringComparison.OrdinalIgnoreCase);
    }
}
