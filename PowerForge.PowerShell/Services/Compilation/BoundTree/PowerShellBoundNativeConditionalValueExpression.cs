namespace PowerForge;

/// <summary>Captures the success records of an authored conditional used as a value.</summary>
internal sealed class PowerShellBoundNativeConditionalValueExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeConditionalValueExpression(SourceSpan span,
        PowerShellBoundNativeConditionalValueClause[] clauses, PowerShellBoundExpression otherwise, bool preserveRecords)
        : base(span,
            new PowerShellTypeFact(preserveRecords ? typeof(object[]) : typeof(object), PowerShellTypeFactProvenance.Inferred,
                "A native conditional value retains zero, one, or multiple success records."),
            PowerShellValueState.Unknown,
            clauses.Aggregate(otherwise.Effects, static (effects, clause) => effects | clause.Condition.Effects | clause.Value.Effects),
            clauses.Aggregate(otherwise.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding,
                static (capabilities, clause) => capabilities | clause.Condition.Capabilities | clause.Value.Capabilities))
    {
        Clauses = clauses;
        Otherwise = otherwise;
        PreserveRecords = preserveRecords;
    }

    internal PowerShellImmutableArray<PowerShellBoundNativeConditionalValueClause> Clauses { get; }
    internal PowerShellBoundExpression Otherwise { get; }
    internal bool PreserveRecords { get; }
}

internal sealed class PowerShellBoundNativeConditionalValueClause
{
    internal PowerShellBoundNativeConditionalValueClause(PowerShellBoundExpression condition, PowerShellBoundExpression value)
    {
        Condition = condition;
        Value = value;
    }

    internal PowerShellBoundExpression Condition { get; }
    internal PowerShellBoundExpression Value { get; }
}
