namespace PowerForge;

/// <summary>Preserves authored conditional-value branch order after semantic lowering.</summary>
internal sealed class PowerShellLoweredNativeConditionalValueExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeConditionalValueExpression(SourceSpan span,
        PowerShellLoweredNativeConditionalValueClause[] clauses, PowerShellLoweredExpression otherwise, bool preserveRecords)
        : base(span, preserveRecords ? typeof(object[]) : typeof(object))
    {
        Clauses = clauses;
        Otherwise = otherwise;
        PreserveRecords = preserveRecords;
    }

    internal PowerShellImmutableArray<PowerShellLoweredNativeConditionalValueClause> Clauses { get; }
    internal PowerShellLoweredExpression Otherwise { get; }
    internal bool PreserveRecords { get; }
}

internal sealed class PowerShellLoweredNativeConditionalValueClause
{
    internal PowerShellLoweredNativeConditionalValueClause(PowerShellLoweredExpression condition, PowerShellLoweredExpression value)
    {
        Condition = condition;
        Value = value;
    }

    internal PowerShellLoweredExpression Condition { get; }
    internal PowerShellLoweredExpression Value { get; }
}
