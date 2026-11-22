namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static PowerShellLoweredStrictLifecycle? LowerStrictLifecycle(PowerShellBoundFunction function,
        IReadOnlyList<PowerShellLoweredStatement> statements, int sharedLocalCount)
    {
        if (function.StrictLifecycle is not { } lifecycle) return null;
        var direct = statements.Skip(sharedLocalCount).ToArray();
        var cleanup = Array.Empty<PowerShellLoweredStatement>();
        var clauses = direct;
        if (lifecycle.HasCleanup)
        {
            var guarded = (PowerShellLoweredTryStatement)direct.Single();
            clauses = guarded.Statements.ToArray();
            cleanup = guarded.FinallyStatements!.Value.ToArray();
        }

        var processIndex = Array.FindIndex(clauses, statement =>
            statement is PowerShellLoweredForEachStatement loop &&
            loop.Variable.Equals(lifecycle.Record) && loop.Span.Equals(lifecycle.ProcessSpan));
        if (processIndex < 0)
            throw new InvalidOperationException("The compiled pipeline stage lost its process clause.");

        // Direct calls use the declared output collector. Attached calls route its
        // records to the next stage and omit both collector setup and finalization.
        var collectorStatements = lifecycle.Collector is null ? 0 : 1;
        var end = clauses.Skip(processIndex + 1).Take(clauses.Length - processIndex - 1 - collectorStatements)
            .Select(static statement => statement is PowerShellLoweredReturnStatement { EmitsValue: true, Expression: { } record }
                ? new PowerShellLoweredExpressionStatement(statement.Span, record, discardValue: false, attachedLifecycleRecord: record)
                : statement)
            .ToArray();
        return new PowerShellLoweredStrictLifecycle(lifecycle.Attachment, lifecycle.Limit,
            statements.Take(sharedLocalCount).ToArray(), direct,
            clauses.Skip(collectorStatements).Take(processIndex - collectorStatements).ToArray(),
            (PowerShellLoweredForEachStatement)clauses[processIndex],
            end,
            cleanup);
    }
}
