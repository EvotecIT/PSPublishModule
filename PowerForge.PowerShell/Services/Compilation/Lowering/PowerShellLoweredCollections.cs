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
    internal PowerShellLoweredExpression[] Elements { get; }
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
    internal PowerShellLoweredDictionaryEntry[] Entries { get; }
}

internal sealed class PowerShellLoweredIndexExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredIndexExpression(SourceSpan span, Type clrType, PowerShellLoweredExpression target, PowerShellLoweredExpression index, PowerShellBoundIndexKind kind)
        : base(span, clrType)
    {
        Target = target;
        Index = index;
        Kind = kind;
    }

    internal PowerShellLoweredExpression Target { get; }
    internal PowerShellLoweredExpression Index { get; }
    internal PowerShellBoundIndexKind Kind { get; }
}

internal sealed class PowerShellLoweredIndexAssignmentStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredIndexAssignmentStatement(SourceSpan span, PowerShellLoweredExpression target, PowerShellLoweredExpression index, PowerShellLoweredExpression value, PowerShellBoundIndexKind kind)
        : base(span)
    {
        Target = target;
        Index = index;
        Value = value;
        Kind = kind;
    }

    internal PowerShellLoweredExpression Target { get; }
    internal PowerShellLoweredExpression Index { get; }
    internal PowerShellLoweredExpression Value { get; }
    internal PowerShellBoundIndexKind Kind { get; }
}
