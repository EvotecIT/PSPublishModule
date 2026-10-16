using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Recognizes source-path syntax without executing commands or treating their runtime binding as trusted.</summary>
internal static class PowerShellCompilationSourcePathPlanner
{
    private static readonly Regex ScriptRootPath = new(
        @"^\$(?:\{)?PSScriptRoot(?:\})?(?<suffix>[\\/].+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Reads a script-root literal; callers retain their own wildcard and containment policy.</summary>
    internal static bool TryReadScriptRootLiteral(CommandElementAst? expression, out string relativePath)
    {
        relativePath = string.Empty;
        if (expression is not ExpandableStringExpressionAst { NestedExpressions.Count: 1 } expandable ||
            expandable.NestedExpressions[0] is not VariableExpressionAst variable ||
            !IsScriptRoot(variable)) return false;
        var match = ScriptRootPath.Match(expandable.Value);
        if (!match.Success) return false;
        relativePath = match.Groups["suffix"].Value.TrimStart('\\', '/');
        return !string.IsNullOrWhiteSpace(relativePath) && relativePath.IndexOf('\0') < 0;
    }

    /// <summary>Plans one candidate target. A derived candidate requires an exact-path and payload check at runtime.</summary>
    internal static bool TryPlan(ScriptBlockAst root, CommandAst command, bool allowDerivedPaths,
        out PowerShellCompilationSourcePathFact? fact)
    {
        fact = null;
        if (command.InvocationOperator != TokenKind.Dot || command.CommandElements.Count == 0) return false;
        var operand = command.CommandElements[0];
        if (TryReadScriptRootLiteral(operand, out var literal))
        {
            fact = new PowerShellCompilationSourcePathFact(operand, literal, producer: null);
            return true;
        }
        if (!allowDerivedPaths || operand is not VariableExpressionAst variable || variable.Splatted ||
            !variable.VariablePath.IsUnqualified ||
            command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            !ReferenceEquals(pipeline.Parent, root.EndBlock)) return false;

        // These are staging candidates, not a constant-value or command-binding proof.
        // Explicit contradictory writes are refused. Calls and their indirect mutations still
        // execute unchanged; the load guard checks the actual operand rather than trusting this syntax.
        var producers = root.EndBlock!.Statements.OfType<AssignmentStatementAst>()
            .Where(assignment => assignment.Extent.EndOffset < command.Extent.StartOffset &&
                                 assignment.Operator == TokenKind.Equals &&
                                 assignment.Left is VariableExpressionAst target && target.VariablePath.IsUnqualified &&
                                 target.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => (Assignment: assignment, Path: ReadJoinPathCandidate(assignment.Right)))
            .Where(static candidate => candidate.Path is not null)
            .ToArray();
        if (producers.Length != 1) return false;
        if (HasInterveningWrite(root, variable.VariablePath.UserPath, producers[0].Assignment.Extent.EndOffset,
                command.Extent.StartOffset)) return false;
        fact = new PowerShellCompilationSourcePathFact(operand, producers[0].Path!, producers[0].Assignment);
        return true;
    }

    private static string? ReadJoinPathCandidate(StatementAst statement)
    {
        if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandAst { CommandElements.Count: 3 } command ||
            command.InvocationOperator is TokenKind.Dot or TokenKind.Ampersand || command.Redirections.Count != 0)
            return null;
        var name = command.GetCommandName();
        if (!string.Equals(name, "Join-Path", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Microsoft.PowerShell.Management\\Join-Path", StringComparison.OrdinalIgnoreCase)) return null;
        if (command.CommandElements[1] is not VariableExpressionAst root || !IsScriptRoot(root) ||
            command.CommandElements[2] is not StringConstantExpressionAst child ||
            string.IsNullOrWhiteSpace(child.Value) || child.Value.IndexOfAny(new[] { '\0', ':' }) >= 0 ||
            child.Value.StartsWith("/", StringComparison.Ordinal) || child.Value.StartsWith("\\", StringComparison.Ordinal))
            return null;
        return child.Value;
    }

    private static bool IsScriptRoot(VariableExpressionAst variable)
        => !variable.Splatted && variable.VariablePath.IsUnqualified &&
           variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase);

    private static bool HasInterveningWrite(ScriptBlockAst root, string name, int producerEnd, int consumerStart)
        => root.FindAll(node => node.Extent.StartOffset >= producerEnd && node.Extent.StartOffset < consumerStart &&
                (node is AssignmentStatementAst assignment &&
                    PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, nativeAttributes: true) is { } assigned &&
                    SameVariable(assigned, name) ||
                 node is UnaryExpressionAst unary && unary.TokenKind is TokenKind.PlusPlus or TokenKind.MinusMinus &&
                    unary.Child is VariableExpressionAst mutated && SameVariable(mutated, name) ||
                 node is ForEachStatementAst loop && SameVariable(loop.Variable, name)),
                searchNestedScriptBlocks: false).Any();

    private static bool SameVariable(VariableExpressionAst variable, string name)
        => PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(variable.VariablePath.UserPath)
            .Equals(name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Records the selected syntax and candidate path independently of any packaged or native publisher.</summary>
internal sealed class PowerShellCompilationSourcePathFact
{
    internal PowerShellCompilationSourcePathFact(CommandElementAst operand, string relativePath, AssignmentStatementAst? producer)
    {
        Operand = operand;
        RelativePath = relativePath;
        Producer = producer;
    }

    internal CommandElementAst Operand { get; }
    internal string RelativePath { get; }
    internal AssignmentStatementAst? Producer { get; }
}
