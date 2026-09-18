using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>A local value transferred across a retained PowerShell and typed-region boundary.</summary>
public sealed class PowerShellCompiledRegionLocal
{
    /// <summary>Creates immutable metadata using the original local-value transfer contract.</summary>
    public PowerShellCompiledRegionLocal(string name, string typeName, bool hasTypeConstraint, string typeConstraintSyntax = "")
        : this(name, typeName, hasTypeConstraint, typeConstraintSyntax, contract: null, alternatives: null)
    {
    }

    /// <summary>Creates immutable metadata with its closed local-value transfer contract.</summary>
    [JsonConstructor]
    public PowerShellCompiledRegionLocal(
        string name,
        string typeName,
        bool hasTypeConstraint,
        string typeConstraintSyntax,
        PowerShellRegionTransferContract? contract,
        IReadOnlyList<PowerShellCompiledRegionLocalAlternative>? alternatives = null)
    {
        Name = name ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        HasTypeConstraint = hasTypeConstraint;
        TypeConstraintSyntax = typeConstraintSyntax ?? string.Empty;
        Contract = contract;
        Alternatives = Array.AsReadOnly((alternatives ?? Array.Empty<PowerShellCompiledRegionLocalAlternative>()).ToArray());
    }

    /// <summary>Unqualified authored local variable name.</summary>
    public string Name { get; }
    /// <summary>CLR type of the transferred value.</summary>
    public string TypeName { get; }
    /// <summary>Whether the receiving variable retains an authored type constraint.</summary>
    public bool HasTypeConstraint { get; }
    /// <summary>Validated authored PowerShell type constraint, including brackets, or empty when unconstrained.</summary>
    public string TypeConstraintSyntax { get; }
    /// <summary>Generalized direction, ownership, output, and mutation contract for this local crossing.</summary>
    public PowerShellRegionTransferContract? Contract { get; }
    /// <summary>Exact branch-dependent constraints carried by a closed alternative envelope.</summary>
    public IReadOnlyList<PowerShellCompiledRegionLocalAlternative> Alternatives { get; }
}
