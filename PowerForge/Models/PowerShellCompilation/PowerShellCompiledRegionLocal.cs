using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>A scalar local restored to the retained PowerShell function after a typed prefix.</summary>
public sealed class PowerShellCompiledRegionLocal
{
    /// <summary>Creates an immutable scalar continuation transfer.</summary>
    [JsonConstructor]
    public PowerShellCompiledRegionLocal(string name, string typeName, bool hasTypeConstraint, string typeConstraintSyntax = "")
    {
        Name = name ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        HasTypeConstraint = hasTypeConstraint;
        TypeConstraintSyntax = typeConstraintSyntax ?? string.Empty;
    }

    /// <summary>Unqualified authored local variable name.</summary>
    public string Name { get; }
    /// <summary>CLR scalar type of the transferred value.</summary>
    public string TypeName { get; }
    /// <summary>Whether the receiving variable retains an authored type constraint.</summary>
    public bool HasTypeConstraint { get; }
    /// <summary>Validated authored PowerShell type constraint, including brackets, or empty when unconstrained.</summary>
    public string TypeConstraintSyntax { get; }
}
