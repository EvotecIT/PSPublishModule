namespace PowerForge;

internal sealed class PowerShellLoweredArrayExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredArrayExpression(SourceSpan span, Type clrType, PowerShellBoundArrayKind kind, PowerShellLoweredExpression[] elements)
        : base(span, clrType)
    {
        Kind = kind;
        Elements = elements;
    }

    internal PowerShellBoundArrayKind Kind { get; }
    internal PowerShellImmutableArray<PowerShellLoweredExpression> Elements { get; }
}

internal sealed class PowerShellLoweredArrayConcatenationExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredArrayConcatenationExpression(
        SourceSpan span,
        PowerShellLoweredExpression left,
        PowerShellLoweredExpression right,
        bool enumerateRight)
        : base(span, typeof(object[]))
    {
        Left = left;
        Right = right;
        EnumerateRight = enumerateRight;
    }

    internal PowerShellLoweredExpression Left { get; }
    internal PowerShellLoweredExpression Right { get; }
    internal bool EnumerateRight { get; }
}

internal sealed class PowerShellLoweredDictionaryEntry
{
    internal PowerShellLoweredDictionaryEntry(PowerShellLoweredExpression key, PowerShellLoweredExpression value)
    {
        Key = key;
        Value = value;
    }

    internal PowerShellLoweredExpression Key { get; }
    internal PowerShellLoweredExpression Value { get; }
}

internal sealed class PowerShellLoweredDictionaryExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredDictionaryExpression(SourceSpan span, Type clrType, PowerShellBoundDictionaryKind kind, PowerShellLoweredDictionaryEntry[] entries)
        : base(span, clrType)
    {
        Kind = kind;
        Entries = entries;
    }

    internal PowerShellBoundDictionaryKind Kind { get; }
    internal PowerShellImmutableArray<PowerShellLoweredDictionaryEntry> Entries { get; }
}

internal sealed class PowerShellLoweredIndexExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredIndexExpression(
        SourceSpan span,
        Type clrType,
        PowerShellLoweredExpression target,
        PowerShellLoweredExpression index,
        PowerShellBoundIndexKind kind,
        bool usePowerShellRuntimeErrors,
        string targetTemporary,
        string indexTemporary)
        : base(span, clrType)
    {
        Target = target;
        Index = index;
        Kind = kind;
        UsePowerShellRuntimeErrors = usePowerShellRuntimeErrors;
        TargetTemporary = targetTemporary;
        IndexTemporary = indexTemporary;
    }

    internal PowerShellLoweredExpression Target { get; }
    internal PowerShellLoweredExpression Index { get; }
    internal PowerShellBoundIndexKind Kind { get; }
    internal bool UsePowerShellRuntimeErrors { get; }
    internal string TargetTemporary { get; }
    internal string IndexTemporary { get; }
}

internal sealed class PowerShellLoweredIndexAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredIndexAssignmentStatement(
        SourceSpan span,
        PowerShellLoweredExpression target,
        PowerShellLoweredExpression index,
        PowerShellLoweredExpression value,
        PowerShellBoundIndexKind kind,
        bool usePowerShellRuntimeErrors,
        string valueTemporary,
        string targetTemporary,
        string indexTemporary)
        : base(span)
    {
        Target = target;
        Index = index;
        Value = value;
        Kind = kind;
        UsePowerShellRuntimeErrors = usePowerShellRuntimeErrors;
        ValueTemporary = valueTemporary;
        TargetTemporary = targetTemporary;
        IndexTemporary = indexTemporary;
    }

    internal PowerShellLoweredExpression Target { get; }
    internal PowerShellLoweredExpression Index { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal PowerShellBoundIndexKind Kind { get; }
    internal bool UsePowerShellRuntimeErrors { get; }
    internal string ValueTemporary { get; }
    internal string TargetTemporary { get; }
    internal string IndexTemporary { get; }
}
