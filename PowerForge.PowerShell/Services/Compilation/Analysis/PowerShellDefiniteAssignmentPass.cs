namespace PowerForge;

/// <summary>Reports local reads and compound mutations that are not definitely assigned on every reachable path.</summary>
internal sealed class PowerShellDefiniteAssignmentPass : IPowerShellSemanticPass
{
    public string Id => "10-definite-assignment";

    public PowerShellBoundProgram Run(PowerShellBoundProgram program)
    {
        var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
        foreach (var function in program.Functions)
        {
            var assigned = function.Parameters.Select(static parameter => parameter.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
            var locals = function.Locals.Select(static local => local.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
            Analyze(function.Body, assigned, locals, diagnostics);
        }
        return program.WithDiagnostics(PowerShellSemanticAnalyzer.OrderDiagnostics(diagnostics));
    }

    private static void Analyze(
        PowerShellBoundBlock block,
        ISet<string> assigned,
        ISet<string> locals,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses) ReportReads(clause.Condition, assigned, locals, diagnostics);
                var branchStates = conditional.Clauses.Select(clause =>
                {
                    var state = assigned.ToHashSet(StringComparer.Ordinal);
                    Analyze(clause.Body, state, locals, diagnostics);
                    return state;
                }).ToList();
                if (conditional.ElseBlock is null)
                    branchStates.Add(assigned.ToHashSet(StringComparer.Ordinal));
                else
                {
                    var elseState = assigned.ToHashSet(StringComparer.Ordinal);
                    Analyze(conditional.ElseBlock, elseState, locals, diagnostics);
                    branchStates.Add(elseState);
                }
                MergeBranchStates(assigned, branchStates);
                continue;
            }
            if (statement is PowerShellBoundWhileStatement loop)
            {
                var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                if (loop.Kind == PowerShellBoundLoopKind.While)
                    ReportReads(loop.Condition, assigned, locals, diagnostics);
                Analyze(loop.Body, loopState, locals, diagnostics);
                if (loop.Kind != PowerShellBoundLoopKind.While)
                {
                    var hasFlowTransfer = HasFlowTransfer(loop.Body);
                    ReportReads(loop.Condition, hasFlowTransfer ? assigned : loopState, locals, diagnostics);
                    if (!hasFlowTransfer)
                    {
                        assigned.Clear();
                        assigned.UnionWith(loopState);
                    }
                }
                continue;
            }
            if (statement is PowerShellBoundForStatement forLoop)
            {
                if (forLoop.Initializer is not null)
                {
                    ReportReads(forLoop.Initializer, assigned, locals, diagnostics);
                    if (forLoop.Initializer.Operation == PowerShellBoundMutationOperator.Assign)
                        assigned.Add(forLoop.Initializer.Target.StableKey);
                }
                if (forLoop.Condition is not null) ReportReads(forLoop.Condition, assigned, locals, diagnostics);
                var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                Analyze(forLoop.Body, loopState, locals, diagnostics);
                if (forLoop.Iterator is not null) ReportReads(forLoop.Iterator, loopState, locals, diagnostics);
                continue;
            }
            if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                ReportReads(forEachLoop.Collection, assigned, locals, diagnostics);
                if (forEachLoop.NullCollectionElement is not null)
                    ReportReads(forEachLoop.NullCollectionElement, assigned, locals, diagnostics);
                var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                loopState.Add(forEachLoop.Variable.StableKey);
                Analyze(forEachLoop.Body, loopState, locals, diagnostics);
                continue;
            }
            if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                ReportReads(switchStatement.Value, assigned, locals, diagnostics);
                foreach (var clause in switchStatement.Clauses) ReportReads(clause.Value, assigned, locals, diagnostics);
                var branchStates = switchStatement.Clauses.Select(clause =>
                {
                    var state = assigned.ToHashSet(StringComparer.Ordinal);
                    Analyze(clause.Body, state, locals, diagnostics);
                    return state;
                }).ToList();
                if (switchStatement.DefaultBlock is null)
                    branchStates.Add(assigned.ToHashSet(StringComparer.Ordinal));
                else
                {
                    var defaultState = assigned.ToHashSet(StringComparer.Ordinal);
                    Analyze(switchStatement.DefaultBlock, defaultState, locals, diagnostics);
                    branchStates.Add(defaultState);
                }
                MergeBranchStates(assigned, branchStates);
                continue;
            }
            if (statement is PowerShellBoundTryStatement tryStatement)
            {
                var branchStates = new List<HashSet<string>>();
                var tryState = assigned.ToHashSet(StringComparer.Ordinal);
                Analyze(tryStatement.Body, tryState, locals, diagnostics);
                branchStates.Add(tryState);
                foreach (var clause in tryStatement.Catches)
                {
                    var catchState = assigned.ToHashSet(StringComparer.Ordinal);
                    Analyze(clause.Body, catchState, locals, diagnostics);
                    branchStates.Add(catchState);
                }
                var definitelyAssigned = branchStates.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
                if (tryStatement.FinallyBlock is not null)
                    Analyze(tryStatement.FinallyBlock, definitelyAssigned, locals, diagnostics);
                assigned.Clear();
                assigned.UnionWith(definitelyAssigned);
                continue;
            }

            var expression = PowerShellSemanticAnalyzer.GetExpression(statement);
            if (expression is not null) ReportReads(expression, assigned, locals, diagnostics);
            if (statement is PowerShellBoundAssignmentStatement assignment)
            {
                if (assignment.Operation != PowerShellBoundMutationOperator.Assign &&
                    locals.Contains(assignment.Target.StableKey) &&
                    !assigned.Contains(assignment.Target.StableKey))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSD1001",
                        $"Local variable '${assignment.Target.Name}' may remain unassigned before this compound mutation.",
                        assignment.Span));
                }
                assigned.Add(assignment.Target.StableKey);
            }
            else if (statement is PowerShellBoundCommandCaptureStatement capture)
            {
                assigned.Add(capture.Target.StableKey);
            }
        }
    }

    private static void MergeBranchStates(ISet<string> assigned, IReadOnlyList<HashSet<string>> branchStates)
    {
        if (branchStates.Count == 0) return;
        var definitelyAssigned = branchStates.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        assigned.Clear();
        assigned.UnionWith(definitelyAssigned);
    }

    private static bool HasFlowTransfer(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block).Any(static statement =>
            statement is PowerShellBoundBreakStatement or PowerShellBoundContinueStatement or
                PowerShellBoundReturnStatement or PowerShellBoundThrowStatement);

    private static void ReportReads(
        PowerShellBoundExpression expression,
        ISet<string> assigned,
        ISet<string> locals,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        foreach (var read in PowerShellSemanticAnalyzer.EnumerateVariableReads(expression).Where(read => locals.Contains(read.Symbol.StableKey)))
        {
            if (assigned.Contains(read.Symbol.StableKey)) continue;
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSD1001",
                $"Local variable '${read.Symbol.Name}' is read before it is definitely assigned and may remain unassigned on at least one reachable path.",
                read.Span));
        }
    }
}
