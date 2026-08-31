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
            PowerShellValueState.Known,
            elements?.Aggregate(PowerShellSemanticEffect.None, static (effects, element) => effects | element.Effects) ?? PowerShellSemanticEffect.None,
            elements?.Aggregate(PowerShellRequiredCapability.None, static (capabilities, element) => capabilities | element.Capabilities) ?? PowerShellRequiredCapability.None)
    {
        Kind = kind;
        Elements = elements;
    }

    internal PowerShellBoundArrayKind Kind { get; }
    internal PowerShellImmutableArray<PowerShellBoundExpression> Elements { get; }
}

internal sealed class PowerShellBoundArrayConcatenationExpression : PowerShellBoundExpression
{
    internal PowerShellBoundArrayConcatenationExpression(
        SourceSpan span,
        PowerShellBoundExpression left,
        PowerShellBoundExpression right,
        bool enumerateRight)
        : base(
            span,
            new PowerShellTypeFact(typeof(object[]), PowerShellTypeFactProvenance.Inferred, "PowerShell array concatenation materializes one Object array."),
            PowerShellValueState.Known,
            left.Effects | right.Effects,
            left.Capabilities | right.Capabilities)
    {
        Left = left;
        Right = right;
        EnumerateRight = enumerateRight;
    }

    internal PowerShellBoundExpression Left { get; }
    internal PowerShellBoundExpression Right { get; }
    internal bool EnumerateRight { get; }
}

internal enum PowerShellBoundDictionaryKind
{
    StringDictionary,
    OrderedStringDictionary,
    ObjectDictionary,
    OrderedObjectDictionary
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
    internal PowerShellBoundDictionaryExpression(SourceSpan span, PowerShellTypeFact type, PowerShellBoundDictionaryKind kind, PowerShellBoundDictionaryEntry[] entries)
        : base(
            span,
            type,
            PowerShellValueState.Known,
            entries.Aggregate(PowerShellSemanticEffect.None, static (effects, entry) => effects | entry.Key.Effects | entry.Value.Effects),
            entries.Aggregate(
                PowerShellRequiredCapability.None,
                static (capabilities, entry) => capabilities | entry.Key.Capabilities | entry.Value.Capabilities))
    {
        Kind = kind;
        Entries = entries;
    }

    internal PowerShellBoundDictionaryKind Kind { get; }
    internal PowerShellImmutableArray<PowerShellBoundDictionaryEntry> Entries { get; }
}

internal enum PowerShellBoundIndexKind
{
    String,
    Array,
    List,
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
        bool usePowerShellRuntimeErrors,
        PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown, target.Effects | index.Effects, target.Capabilities | index.Capabilities)
    {
        Target = target;
        Index = index;
        Kind = kind;
        UsePowerShellRuntimeErrors = usePowerShellRuntimeErrors;
    }

    internal PowerShellBoundExpression Target { get; }
    internal PowerShellBoundExpression Index { get; }
    internal PowerShellBoundIndexKind Kind { get; }
    internal bool UsePowerShellRuntimeErrors { get; }
}

internal sealed class PowerShellBoundIndexAssignmentStatement : PowerShellBoundStatement
{
    internal PowerShellBoundIndexAssignmentStatement(
        SourceSpan span,
        PowerShellBoundExpression target,
        PowerShellBoundExpression index,
        PowerShellBoundExpression value,
        PowerShellBoundIndexKind kind,
        bool usePowerShellRuntimeErrors)
        : base(
            span,
            PowerShellSemanticEffect.Mutation | target.Effects | index.Effects | value.Effects,
            target.Capabilities | index.Capabilities | value.Capabilities)
    {
        Target = target;
        Index = index;
        Value = value;
        Kind = kind;
        UsePowerShellRuntimeErrors = usePowerShellRuntimeErrors;
    }

    internal PowerShellBoundExpression Target { get; }
    internal PowerShellBoundExpression Index { get; }
    internal PowerShellBoundExpression Value { get; }
    internal PowerShellBoundIndexKind Kind { get; }
    internal bool UsePowerShellRuntimeErrors { get; }
}
