namespace PowerForge;

/// <summary>
/// Result of scanning a binary PowerShell assembly for exported cmdlets and aliases.
/// </summary>
public sealed class PowerShellAssemblyMetadataResult
{
    /// <summary>
    /// Creates a new metadata result.
    /// </summary>
    public PowerShellAssemblyMetadataResult(string[] cmdletsToExport, string[] aliasesToExport)
    {
        CmdletsToExport = cmdletsToExport ?? Array.Empty<string>();
        AliasesToExport = aliasesToExport ?? Array.Empty<string>();
    }

    /// <summary>Cmdlets discovered in the assembly.</summary>
    public string[] CmdletsToExport { get; }

    /// <summary>Aliases discovered in the assembly.</summary>
    public string[] AliasesToExport { get; }
}
