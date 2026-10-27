using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Validates the opt-in process-shared assembly boundary for generated module loaders.</summary>
internal static class SharedAssemblyOptions
{
    /// <summary>
    /// Validates exact simple names, the preload subset, and separation from binary exports.
    /// Shared dependencies must maintain a compatible ABI for the lifetime of a process.
    /// </summary>
    internal static void Validate(
        IReadOnlyList<string>? sharedAssemblies,
        IReadOnlyList<string>? preloadSharedAssemblies,
        IReadOnlyList<string>? exportAssemblies)
    {
        var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sharedAssemblies ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z") ||
                name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".", StringComparison.Ordinal) || name.Contains("..") ||
                IsHostAssembly(name))
                throw new ArgumentException("SharedAssemblies requires exact non-system assembly simple names, without paths, extensions or wildcards.");
            if (!shared.Add(name))
                throw new ArgumentException("SharedAssemblies contains a duplicate name: " + name);
        }
        foreach (var name in preloadSharedAssemblies ?? Array.Empty<string>())
            if (name is null || !shared.Contains(name))
                throw new ArgumentException("PreloadSharedAssemblies must be a subset of SharedAssemblies.");
        foreach (var export in exportAssemblies ?? Array.Empty<string>())
            if (shared.Contains(Path.GetFileNameWithoutExtension(export)))
                throw new ArgumentException("Shared assemblies are dependencies and cannot also be binary exports: " + export);
    }

    private static bool IsHostAssembly(string name)
        => name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Microsoft.PowerShell", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Microsoft.Win32", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("Microsoft.Win32.", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase);
}
