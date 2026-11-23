namespace PowerForge;

/// <summary>Reads an invocation-owned value without inferring a CLR slot from its authored constraint.</summary>
internal sealed class PowerShellBoundNativeVariableExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeVariableExpression(SourceSpan span, string name, bool inExpandableString, string sourcePath, string sourceText, bool directLocal)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
            PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Name = name;
        InExpandableString = inExpandableString;
        SourcePath = sourcePath;
        SourceText = sourceText;
        DirectLocal = directLocal;
    }

    internal string Name { get; }
    internal bool InExpandableString { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
    internal bool DirectLocal { get; }
}

/// <summary>Executes an assignment through invocation-owned storage and its existing constraints.</summary>
internal sealed class PowerShellBoundNativeAssignmentStatement : PowerShellBoundStatement
{
    internal PowerShellBoundNativeAssignmentStatement(SourceSpan span, string name, PowerShellBoundExpression value,
        PowerShellBoundMutationOperator operation, PowerShellNativeAssignmentTarget target,
        bool closesNativeLocalCallBinding = false)
        : base(span, value.Effects | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError,
            value.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Name = name;
        Value = value;
        Target = target;
        Operation = operation;
        ClosesNativeLocalCallBinding = closesNativeLocalCallBinding;
    }

    internal string Name { get; }
    internal PowerShellBoundExpression Value { get; }
    internal PowerShellNativeAssignmentTarget Target { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal bool ClosesNativeLocalCallBinding { get; }
}
