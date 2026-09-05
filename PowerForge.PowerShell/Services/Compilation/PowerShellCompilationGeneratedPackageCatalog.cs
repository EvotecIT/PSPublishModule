using System.Text.Json;

namespace PowerForge;

/// <summary>Owns immutable package identities used by compiler-generated projects.</summary>
internal static class PowerShellCompilationGeneratedPackageCatalog
{
    private const string ResourceName = "PowerForge.PowerShell.Compilation.CompilerPackages.json";
    private static readonly Lazy<PackageIdentity[]> Packages = new(ReadPackages);

    internal static PackageIdentity[] Select(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        string? targetFramework)
    {
        var framework = targetFramework ?? string.Empty;
        var selected = new List<PackageIdentity>();
        if (framework.Equals("net472", StringComparison.OrdinalIgnoreCase) &&
            kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
        {
            selected.Add(Find("Microsoft.NETFramework.ReferenceAssemblies", "1.0.3"));
        }

        if (kind == PowerShellCompilationArtifactKind.BinaryModule)
        {
            if (framework.Equals("net472", StringComparison.OrdinalIgnoreCase))
            {
                selected.Add(Find("Microsoft.PowerShell.5.ReferenceAssemblies", "1.1.0"));
            }
            else
            {
                AddPowerShellRuntimePackages(selected, framework);
            }
        }
        else if (kind == PowerShellCompilationArtifactKind.Executable &&
                 mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid)
        {
            AddPowerShellRuntimePackages(selected, framework);
        }

        return selected
            .Distinct(PackageIdentityComparer.Instance)
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddPowerShellRuntimePackages(ICollection<PackageIdentity> packages, string framework)
    {
        var sdkVersion = framework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "7.6.5" : "7.4.18";
        packages.Add(Find("Microsoft.PowerShell.SDK", sdkVersion));
        packages.Add(Find("System.Security.Cryptography.Xml", "10.0.11"));
    }

    private static PackageIdentity Find(string id, string version)
        => Packages.Value.SingleOrDefault(package =>
               package.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
               package.Version.Equals(version, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"Compiler package catalog does not contain immutable identity '{id}/{version}'.");

    private static PackageIdentity[] ReadPackages()
    {
        using var stream = typeof(PowerShellCompilationGeneratedPackageCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded compiler package catalog '{ResourceName}' was not found.");
        var document = JsonSerializer.Deserialize<PackageCatalogDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Compiler package catalog is empty.");
        foreach (var package in document.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Version) || string.IsNullOrWhiteSpace(package.ContentHash))
                throw new InvalidOperationException("Compiler package catalog contains an incomplete immutable package identity.");
            try
            {
                if (Convert.FromBase64String(package.ContentHash).Length != 64)
                    throw new InvalidOperationException($"Compiler package '{package.Id}/{package.Version}' does not have a SHA-512 NuGet content hash.");
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"Compiler package '{package.Id}/{package.Version}' has an invalid NuGet content hash.", exception);
            }
        }
        return document.Packages;
    }

    internal sealed class PackageIdentity
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
    }

    private sealed class PackageCatalogDocument
    {
        public PackageIdentity[] Packages { get; set; } = Array.Empty<PackageIdentity>();
    }

    private sealed class PackageIdentityComparer : IEqualityComparer<PackageIdentity>
    {
        internal static PackageIdentityComparer Instance { get; } = new();

        public bool Equals(PackageIdentity? left, PackageIdentity? right)
            => ReferenceEquals(left, right) || left is not null && right is not null &&
               left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase) &&
               left.Version.Equals(right.Version, StringComparison.Ordinal);

        public int GetHashCode(PackageIdentity package)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(package.Id) * 397 ^ StringComparer.Ordinal.GetHashCode(package.Version);
    }
}
