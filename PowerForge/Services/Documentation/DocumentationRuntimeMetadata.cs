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

    /// <summary>Canonical CLR type identity observed inside the PowerShell host.</summary>
    [DataMember(Name = "canonicalTypeName")]
    public string? CanonicalTypeName { get; set; }

    /// <summary>Recursively captured collection elements.</summary>
    [DataMember(Name = "items")]
    public List<DocumentationRuntimeValue> Items { get; set; } = new();
}
