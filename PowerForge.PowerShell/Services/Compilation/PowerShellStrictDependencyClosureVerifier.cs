using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

/// <summary>
/// Mechanically proves that a Strict runtime-free artifact does not ship PowerShell source,
/// reference the PowerShell runtime, or retain known dynamic-evaluation entry points.
/// </summary>
internal static class PowerShellStrictDependencyClosureVerifier
{
    private static readonly string[] ForbiddenSourceTokens =
    {
        "System.Management.Automation",
        "ScriptBlock.Create",
        "PowerShell.Create"
    };

    internal static bool Verify(IEnumerable<PowerShellCompilationArtifactFile> files)
    {
        foreach (var file in files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsPowerShellSource(file.Path))
                throw new InvalidOperationException($"Strict runtime-free artifact contains PowerShell source '{file.Path}'.");

            if (Path.GetExtension(file.Path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                VerifyGeneratedSource(file.Path);

            if (Path.GetExtension(file.Path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                VerifyManagedAssemblyReferences(file.Path);
        }

        return true;
    }

    internal static bool IsPowerShellSource(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyGeneratedSource(string path)
    {
        var source = File.ReadAllText(path);
        foreach (var token in ForbiddenSourceTokens)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"Strict runtime-free generated source '{path}' contains forbidden PowerShell runtime token '{token}'.");
        }
    }

    private static void VerifyManagedAssemblyReferences(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return;

            var reader = pe.GetMetadataReader();
            foreach (var referenceHandle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(referenceHandle);
                var name = reader.GetString(reference.Name);
                if (name.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Microsoft.PowerShell.SDK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Strict runtime-free managed dependency '{path}' references forbidden PowerShell assembly '{name}'.");
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Native executables and platform files can share managed extensions.
        }
    }
}
