using System.Text.Json;

namespace PowerForge;

/// <summary>Reconciles an actual generated-project NuGet restore with the reviewed compiler package lock.</summary>
internal static class PowerShellCompilationResolvedPackageCatalog
{
    internal static PowerShellCompilationResolvedPackage[] ReadAndVerify(
        string workspace,
        PowerShellCompilationDependencyGraph graph,
        string? exactLockPath = null)
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

        var result = resolved
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(exactLockPath))
            EnsureExactClosure(result, exactLockPath!);
        return result;
    }

    private static void EnsureExactClosure(
        PowerShellCompilationResolvedPackage[] actual,
        string lockPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!document.RootElement.TryGetProperty("dependencies", out var groups) || groups.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The exact NuGet closure lock has no dependency groups.");
        var expected = new Dictionary<string, PowerShellCompilationResolvedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var package in group.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("resolved", out var versionElement) ||
                    !package.Value.TryGetProperty("contentHash", out var hashElement))
                    throw new InvalidDataException($"NuGet closure entry '{package.Name}' has no exact version or content hash.");
                var version = versionElement.GetString() ?? string.Empty;
                var contentHash = PowerShellCompilationNuGetPackageVerifier.NormalizeContentHash(hashElement.GetString() ?? string.Empty);
                var key = package.Name + "/" + version;
                if (expected.TryGetValue(key, out var duplicate) && !duplicate.ContentHash.Equals(contentHash, StringComparison.Ordinal))
                    throw new InvalidDataException($"The exact NuGet closure lock contains conflicting identities for '{key}'.");
                expected[key] = new PowerShellCompilationResolvedPackage
                {
                    Id = package.Name,
                    Version = version,
                    ContentHashAlgorithm = "SHA-512",
                    ContentHash = contentHash
                };
            }
        }
        var expectedValues = expected.Values
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
        if (actual.Length != expectedValues.Length || actual.Where((package, index) =>
                !package.Id.Equals(expectedValues[index].Id, StringComparison.OrdinalIgnoreCase) ||
                !package.Version.Equals(expectedValues[index].Version, StringComparison.Ordinal) ||
                !package.ContentHash.Equals(expectedValues[index].ContentHash, StringComparison.Ordinal)).Any())
            throw new InvalidDataException("Generated compilation restore closure differs from its exact target packages.lock.json.");
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
