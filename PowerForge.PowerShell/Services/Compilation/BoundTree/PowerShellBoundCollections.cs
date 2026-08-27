namespace PowerForge;

internal enum PowerShellBoundArrayKind
{
    Literal,
    CollectedExpression
}

internal sealed class PowerShellBoundArrayExpression : PowerShellBoundExpression
{
    internal PowerShellBoundArrayExpression(
        SourceSpan span,
        Type arrayType,
        PowerShellBoundArrayKind kind,
        PowerShellBoundExpression[] elements)
        : base(
            span,
            new PowerShellTypeFact(arrayType, PowerShellTypeFactProvenance.Inferred, "The bound array element contract selects one CLR array representation."),
            PowerShellValueState.Known)
    {
        Kind = kind;
        Elements = elements;
    }

    internal PowerShellBoundArrayKind Kind { get; }
    internal PowerShellBoundExpression[] Elements { get; }
}

internal enum PowerShellBoundDictionaryKind
{
    StringDictionary,
    OrderedStringDictionary
}

internal sealed class PowerShellBoundDictionaryEntry
{
    internal PowerShellBoundDictionaryEntry(PowerShellBoundExpression key, PowerShellBoundExpression value)
    {
        Key = key;
        Value = value;
    }

    internal PowerShellBoundExpression Key { get; }
    internal PowerShellBoundExpression Value { get; }
}

internal sealed class PowerShellBoundDictionaryExpression : PowerShellBoundExpression
{
    internal PowerShellBoundDictionaryExpression(SourceSpan span, Type dictionaryType, PowerShellBoundDictionaryKind kind, PowerShellBoundDictionaryEntry[] entries)
        : base(span, new PowerShellTypeFact(dictionaryType, PowerShellTypeFactProvenance.Inferred, "A homogeneous literal selects one case-insensitive CLR dictionary representation."), PowerShellValueState.Known)
    {
        Kind = kind;
        Entries = entries;
    }

    internal PowerShellBoundDictionaryKind Kind { get; }
    internal PowerShellBoundDictionaryEntry[] Entries { get; }
}

internal enum PowerShellBoundIndexKind
{
    String,
    Array,
    StringDictionary,
    OrderedStringDictionary,
    ObjectDictionary
}

internal sealed class PowerShellBoundIndexExpression : PowerShellBoundExpression
{
    internal PowerShellBoundIndexExpression(
        SourceSpan span,
        PowerShellBoundExpression target,
        PowerShellBoundExpression index,
        PowerShellBoundIndexKind kind,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
    {
        Target = target;
        Index = index;
        Kind = kind;
    }

    internal PowerShellBoundExpression Target { get; }
    internal PowerShellBoundExpression Index { get; }
    internal PowerShellBoundIndexKind Kind { get; }
}

internal sealed class PowerShellBoundIndexAssignmentStatement : PowerShellBoundStatement
{
    internal PowerShellBoundIndexAssignmentStatement(
        SourceSpan span,
        PowerShellBoundExpression target,
        PowerShellBoundExpression index,
        PowerShellBoundExpression value,
        PowerShellBoundIndexKind kind)
        : base(span, PowerShellSemanticEffect.Mutation)
    {
        Target = target;
        Index = index;
        Value = value;
        Kind = kind;
    }

    internal PowerShellBoundExpression Target { get; }
    internal PowerShellBoundExpression Index { get; }
    internal PowerShellBoundExpression Value { get; }
    internal PowerShellBoundIndexKind Kind { get; }
}
