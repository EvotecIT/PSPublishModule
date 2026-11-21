using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Recognizes source-path syntax without executing commands or treating their runtime binding as trusted.</summary>
internal static partial class PowerShellCompilationSourcePathPlanner
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
        if (!allowDerivedPaths || command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            !IsRootSourceLoad(root, pipeline)) return false;

        // The staging candidate bounds the source closure. The load guard checks the actual
        // operand after the original command and its possible indirect writes have run.
        if (operand is ParenExpressionAst direct && ReadJoinPathCandidate(root, direct.Pipeline) is { } directPath)
        {
            fact = new PowerShellCompilationSourcePathFact(operand, directPath, producer: null);
            return true;
        }
        if (operand is not VariableExpressionAst { Splatted: false } variable || !variable.VariablePath.IsUnqualified)
            return false;

        var name = variable.VariablePath.UserPath;
        var producers = FindPriorAssignments(root, name, command.Extent.StartOffset)
            .Select(assignment => (Assignment: assignment, Path: ReadJoinPathCandidate(root, assignment.Right)))
            .Where(static candidate => candidate.Path is not null)
            .ToArray();
        if (producers.Length != 1) return false;
        if (HasInterveningWrite(root, name, producers[0].Assignment.Extent.EndOffset, command.Extent.StartOffset))
            return false;
        fact = new PowerShellCompilationSourcePathFact(operand, producers[0].Path!, producers[0].Assignment);
        return true;
    }

    private static bool IsRootSourceLoad(ScriptBlockAst root, PipelineAst pipeline)
        => ReferenceEquals(pipeline.Parent, root.EndBlock) ||
           (pipeline.Parent is StatementBlockAst { Parent: IfStatementAst conditional } &&
            ReferenceEquals(conditional.Parent, root.EndBlock));

    private static string? ReadJoinPathCandidate(ScriptBlockAst root, StatementAst statement)
    {
        if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandAst { CommandElements.Count: 3 } command ||
            command.InvocationOperator is TokenKind.Dot or TokenKind.Ampersand || command.Redirections.Count != 0 ||
            !IsJoinPathCommand(command) ||
            command.CommandElements[1] is not VariableExpressionAst rootVariable || !IsScriptRoot(rootVariable))
            return null;

        var child = ReadJoinPathChild(root, command.CommandElements[2]);
        return child is null || string.IsNullOrWhiteSpace(child) || child.IndexOfAny(new[] { '\0', ':' }) >= 0 ||
            child.StartsWith("/", StringComparison.Ordinal) || child.StartsWith("\\", StringComparison.Ordinal)
            ? null
            : child;
    }

    private static string? ReadJoinPathChild(ScriptBlockAst root, CommandElementAst child)
    {
        if (child is StringConstantExpressionAst literal) return literal.Value;
        if (child is not VariableExpressionAst { Splatted: false } variable || !variable.VariablePath.IsUnqualified)
            return null;

        var name = variable.VariablePath.UserPath;
        var producers = FindPriorAssignments(root, name, child.Extent.StartOffset).Take(2).ToArray();
        if (producers.Length != 1 ||
            HasInterveningWrite(root, name, producers[0].Extent.EndOffset, child.Extent.StartOffset))
            return null;
        var expression = producers[0].Right switch
        {
            CommandExpressionAst command => command.Expression,
            PipelineAst pipeline => pipeline.GetPureExpression(),
            _ => null
        };
        return (expression as StringConstantExpressionAst)?.Value;
    }

    private static IEnumerable<AssignmentStatementAst> FindPriorAssignments(ScriptBlockAst root, string name, int consumerStart)
        => root.EndBlock!.Statements.OfType<AssignmentStatementAst>()
            .Where(assignment => assignment.Extent.EndOffset < consumerStart &&
                assignment.Operator == TokenKind.Equals &&
                assignment.Left is VariableExpressionAst target && target.VariablePath.IsUnqualified &&
                target.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase));

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
