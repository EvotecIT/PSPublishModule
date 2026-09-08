namespace PowerForge;

internal sealed class PowerShellLoweredNativeVariableExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeVariableExpression(SourceSpan span, string name, bool inExpandableString, string sourcePath, string sourceText, bool directLocal) : base(span, typeof(object))
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

internal sealed class PowerShellLoweredNativeVariableAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredNativeVariableAssignmentStatement(SourceSpan span, string name, PowerShellLoweredExpression value) : base(span)
    {
        Name = name;
        Value = value;
    }
    internal string Name { get; }
    internal PowerShellLoweredExpression Value { get; }
}
