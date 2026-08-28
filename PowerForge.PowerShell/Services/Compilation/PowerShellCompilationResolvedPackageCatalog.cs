using System.Text.Json;

namespace PowerForge;

/// <summary>Reconciles an actual generated-project NuGet restore with the reviewed compiler package lock.</summary>
internal static class PowerShellCompilationResolvedPackageCatalog
{
    internal static PowerShellCompilationResolvedPackage[] ReadAndVerify(
        string workspace,
        PowerShellCompilationDependencyGraph graph)
    {
        var assetsPath = Path.Combine(workspace, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            throw new InvalidOperationException("Generated-project restore did not produce obj/project.assets.json for package provenance verification.");

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Generated-project restore assets do not contain a libraries object.");

        var resolved = new List<PowerShellCompilationResolvedPackage>();
        foreach (var library in libraries.EnumerateObject())
        {
            var value = library.Value;
            if (!value.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = library.Name.LastIndexOf('/');
            if (separator <= 0 || separator == library.Name.Length - 1)
                throw new InvalidDataException($"Resolved NuGet package identity '{library.Name}' is malformed.");
            if (!value.TryGetProperty("sha512", out var contentHashElement) || string.IsNullOrWhiteSpace(contentHashElement.GetString()))
                throw new InvalidDataException($"Resolved NuGet package '{library.Name}' has no immutable content hash.");
            var contentHash = contentHashElement.GetString()!;
            try
            {
                if (Convert.FromBase64String(contentHash).Length != 64)
                    throw new InvalidDataException($"Resolved NuGet package '{library.Name}' does not have a SHA-512 content hash.");
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Resolved NuGet package '{library.Name}' has an invalid content hash.", exception);
            }
            resolved.Add(new PowerShellCompilationResolvedPackage
            {
                Id = library.Name.Substring(0, separator),
                Version = library.Name.Substring(separator + 1),
                ContentHashAlgorithm = "SHA-512",
                ContentHash = contentHash
            });
        }

        var reviewed = graph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NuGetPackage &&
                                  node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Build))
            .ToArray();
        foreach (var node in reviewed)
        {
            var package = resolved.SingleOrDefault(candidate =>
                candidate.Id.Equals(node.Identity.Name, StringComparison.OrdinalIgnoreCase) &&
                candidate.Version.Equals(node.Identity.Version, StringComparison.Ordinal));
            if (package is null)
                throw new InvalidOperationException($"Reviewed compiler package '{node.Identity.Name}/{node.Identity.Version}' is absent from the generated restore closure.");
            if (!package.ContentHash.Equals(node.Identity.ContentHash, StringComparison.Ordinal))
                throw new InvalidOperationException($"Resolved compiler package '{package.Id}/{package.Version}' content hash does not match the reviewed dependency lock.");
            package.DirectCompilerReference = true;
        }

        return resolved
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed class PowerShellCompilationResolvedPackage
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ContentHashAlgorithm { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public bool DirectCompilerReference { get; set; }
}
