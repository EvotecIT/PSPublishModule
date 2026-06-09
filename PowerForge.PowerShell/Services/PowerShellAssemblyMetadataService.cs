using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Reads exported cmdlet and alias metadata from a PowerShell binary assembly.
/// </summary>
public sealed class PowerShellAssemblyMetadataService
{
    /// <summary>
    /// Scans the provided assembly and returns discovered cmdlets and aliases.
    /// </summary>
    public PowerShellAssemblyMetadataResult Analyze(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path is required.", nameof(assemblyPath));

        var fullPath = Path.GetFullPath(assemblyPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Assembly not found: {fullPath}", fullPath);

        var cmdlets = BinaryExportDetector.DetectBinaryCmdlets(new[] { fullPath })
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var aliases = BinaryExportDetector.DetectBinaryAliases(new[] { fullPath })
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PowerShellAssemblyMetadataResult(cmdlets, aliases);
    }
}
