using System.Management.Automation;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

/// <summary>Checks whether an array element or generic argument can be named in generated C#.</summary>
internal static class PowerShellGeneratedElementTypePolicy
{
    internal static bool IsRepresentable(Type type, string? targetFramework)
    {
        if (type.IsArray) return IsRepresentable(type.GetElementType()!, targetFramework);
        if (type.IsGenericType && !type.GetGenericArguments().All(argument => IsRepresentable(argument, targetFramework)))
            return false;
        if (!type.IsAbstract || !type.IsSealed) return true;

        // SDK class shape can differ between hosts. A static compiler-host type
        // is not evidence that the Windows PowerShell reference declares it static.
        if (!string.Equals(targetFramework, "net472", StringComparison.OrdinalIgnoreCase) ||
            type.Assembly != typeof(PSObject).Assembly) return false;
        var package = PowerShellCompilationGeneratedPackageCatalog.Select(
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid, targetFramework)
            .Single(package => package.Id.Equals("Microsoft.PowerShell.5.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase));
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var reference = Path.Combine(packageRoot!, package.Id.ToLowerInvariant(), package.Version, "lib", "net4", "System.Management.Automation.dll");
        if (!File.Exists(reference)) return false;
        using var stream = File.OpenRead(reference);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly || !reader.GetString(reader.GetAssemblyDefinition().Name)
                .Equals(typeof(PSObject).Assembly.GetName().Name, StringComparison.Ordinal)) return false;
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (!reader.GetString(definition.Namespace).Equals(type.Namespace, StringComparison.Ordinal) ||
                !reader.GetString(definition.Name).Equals(type.Name, StringComparison.Ordinal) || definition.IsNested) continue;
            return (definition.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) !=
                   (TypeAttributes.Abstract | TypeAttributes.Sealed);
        }
        return false;
    }
}
