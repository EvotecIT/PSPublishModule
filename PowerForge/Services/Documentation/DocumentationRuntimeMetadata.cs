using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PowerForge;

/// <summary>
/// Tagged runtime value captured inside the PowerShell host before documentation
/// metadata is normalized by the host-agnostic PowerForge engine.
/// </summary>
[DataContract]
internal sealed class DocumentationRuntimeValue
{
    /// <summary>Runtime value category used by the C# formatter.</summary>
    [DataMember(Name = "kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Invariant or textual value supplied by the PowerShell host.</summary>
    [DataMember(Name = "text")]
    public string? Text { get; set; }

    /// <summary>Named enum member, when the value represents an enum.</summary>
    [DataMember(Name = "name")]
    public string? Name { get; set; }

    /// <summary>Whether a captured string is the process-wide intern-pool singleton.</summary>
    [DataMember(Name = "isInterned")]
    public bool IsInterned { get; set; }

    /// <summary>Canonical CLR type identity observed inside the PowerShell host.</summary>
    [DataMember(Name = "canonicalTypeName")]
    public string? CanonicalTypeName { get; set; }

    /// <summary>Canonical CLR enum underlying type observed inside the PowerShell host.</summary>
    [DataMember(Name = "underlyingTypeName")]
    public string? UnderlyingTypeName { get; set; }

    /// <summary>UTF-16 code units for the defining assembly identity of a Type value.</summary>
    [DataMember(Name = "assemblyNameCodeUnits")]
    public string? AssemblyNameCodeUnits { get; set; }

    /// <summary>UTF-16 code units for the exact runtime type name when reflection lookup is required.</summary>
    [DataMember(Name = "runtimeTypeNameCodeUnits")]
    public string? RuntimeTypeNameCodeUnits { get; set; }

    /// <summary>Flat structural description of an exact runtime type, including constructed generics.</summary>
    [DataMember(Name = "runtimeTypeShape")]
    public string? RuntimeTypeShape { get; set; }

    /// <summary>Canonical CLR element type identity for an array container.</summary>
    [DataMember(Name = "elementTypeName")]
    public string? ElementTypeName { get; set; }

    /// <summary>Recursively captured collection elements.</summary>
    [DataMember(Name = "items")]
    public List<DocumentationRuntimeValue> Items { get; set; } = new();

    /// <summary>
    /// Flat structural token stream used by the host collector so JSON depth does
    /// not depend on the nesting depth of the captured runtime value.
    /// </summary>
    [DataMember(Name = "tokens")]
    public List<DocumentationRuntimeValue> Tokens { get; set; } = new();
}
