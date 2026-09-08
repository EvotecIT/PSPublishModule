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

/// <summary>Writes an unconstrained assignment through the native variable owner, including existing parameter constraints.</summary>
internal sealed class PowerShellBoundNativeVariableAssignmentStatement : PowerShellBoundStatement
{
    internal PowerShellBoundNativeVariableAssignmentStatement(SourceSpan span, string name, PowerShellBoundExpression value)
        : base(span, value.Effects | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError,
            value.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost |
            PowerShellRequiredCapability.PowerShellStatementErrors)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal PowerShellBoundExpression Value { get; }
}
