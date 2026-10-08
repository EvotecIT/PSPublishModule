using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>One exact CLR and authored-constraint option for a closed continuation local.</summary>
public sealed class PowerShellCompiledRegionLocalAlternative
{
    /// <summary>Creates immutable alternative metadata.</summary>
    [JsonConstructor]
    public PowerShellCompiledRegionLocalAlternative(
        string typeName,
        string typeConstraintSyntax,
        PowerShellRegionTransferContract contract)
    {
        TypeName = typeName ?? string.Empty;
        TypeConstraintSyntax = typeConstraintSyntax ?? string.Empty;
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
    }

    /// <summary>Exact CLR type restored into retained PowerShell.</summary>
    public string TypeName { get; }
    /// <summary>Validated authored PowerShell type constraint, including brackets.</summary>
    public string TypeConstraintSyntax { get; }
    /// <summary>Shape, ownership, mutation, and enumeration contract of this alternative.</summary>
    public PowerShellRegionTransferContract Contract { get; }
}
