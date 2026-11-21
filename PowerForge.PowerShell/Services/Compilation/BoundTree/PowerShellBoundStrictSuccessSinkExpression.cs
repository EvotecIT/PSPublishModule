namespace PowerForge;

/// <summary>The caller's standalone success stream, passed to a compiler-owned lifecycle runner.</summary>
internal sealed class PowerShellBoundStrictSuccessSinkExpression : PowerShellBoundExpression
{
    internal PowerShellBoundStrictSuccessSinkExpression(SourceSpan span, Type recordType) : base(span,
        new PowerShellTypeFact(typeof(Action<object>), PowerShellTypeFactProvenance.CommandContract,
            "A standalone pipeline sends its records to the caller's success stream."),
        PowerShellValueState.Known, PowerShellSemanticEffect.None, PowerShellRequiredCapability.RuntimeFreeSuccessOutput)
        => RecordType = recordType;

    internal Type RecordType { get; }
}
