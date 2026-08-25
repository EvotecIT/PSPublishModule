using System;

namespace PowerForge;

/// <summary>Kinds of target-typed constant values preserved by the compiler.</summary>
public enum PowerShellCompilationLiteralKind
{
    /// <summary>A null reference or nullable value.</summary>
    Null,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>A signed integral value.</summary>
    SignedInteger,
    /// <summary>An unsigned integral value.</summary>
    UnsignedInteger,
    /// <summary>A single- or double-precision floating-point value.</summary>
    FloatingPoint,
    /// <summary>A decimal value.</summary>
    Decimal,
    /// <summary>A character value.</summary>
    Character,
    /// <summary>A string value.</summary>
    String,
    /// <summary>An enum value represented by its invariant underlying integer.</summary>
    Enum,
    /// <summary>A GUID value.</summary>
    Guid,
    /// <summary>A date and time value.</summary>
    DateTime,
    /// <summary>A date, time, and offset value.</summary>
    DateTimeOffset,
    /// <summary>A time interval.</summary>
    TimeSpan,
    /// <summary>A URI value.</summary>
    Uri,
    /// <summary>A version value.</summary>
    Version,
    /// <summary>A one-dimensional target-typed array.</summary>
    Array
}

/// <summary>A safely evaluated, target-typed PowerShell constant.</summary>
public sealed class PowerShellCompilationLiteral
{
    /// <summary>Creates a compilation literal.</summary>
    public PowerShellCompilationLiteral(
        PowerShellCompilationLiteralKind kind,
        string typeName,
        string? value = null,
        PowerShellCompilationLiteral[]? elements = null)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationLiteralKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        TypeName = typeName ?? string.Empty;
        Value = value ?? string.Empty;
        Elements = elements ?? Array.Empty<PowerShellCompilationLiteral>();
    }

    /// <summary>Constant representation kind.</summary>
    public PowerShellCompilationLiteralKind Kind { get; }

    /// <summary>Resolved CLR type name after PowerShell parameter conversion.</summary>
    public string TypeName { get; }

    /// <summary>Invariant scalar representation.</summary>
    public string Value { get; }

    /// <summary>Target-typed array elements.</summary>
    public PowerShellCompilationLiteral[] Elements { get; }
}
