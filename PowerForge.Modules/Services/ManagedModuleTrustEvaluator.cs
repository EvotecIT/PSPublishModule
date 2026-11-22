namespace PowerForge;

/// <summary>
/// Evaluates managed module repository and package metadata against caller-supplied trust policy.
/// </summary>
public static class ManagedModuleTrustEvaluator
{
    internal static bool HasPolicy(ManagedModuleTrustPolicy? policy)
        => policy is not null &&
           (policy.RequireTrustedRepository || NormalizeAuthors(policy.AllowedAuthors).Count > 0);

    internal static bool HasAllowedAuthorPolicy(ManagedModuleTrustPolicy? policy)
        => NormalizeAuthors(policy?.AllowedAuthors).Count > 0;

    /// <summary>
    /// Normalizes caller-supplied package author policy values for stable comparison and output.
    /// </summary>
    public static IReadOnlyList<string> NormalizeAuthors(IReadOnlyList<string>? authors)
    {
        if (authors is null || authors.Count == 0)
            return Array.Empty<string>();

        return authors
            .Where(static author => !string.IsNullOrWhiteSpace(author))
            .Select(static author => author.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static author => author, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static void ThrowIfRepositoryRejected(
        ManagedModuleRepository repository,
        ManagedModuleTrustPolicy? policy)
    {
        if (policy?.RequireTrustedRepository != true || repository.Trusted)
            return;

        throw new ManagedModuleTrustException(
            $"Repository '{repository.Name}' is not trusted by the selected profile or caller policy.",
            moduleName: null,
            version: null,
            repository.Name,
            "RepositoryNotTrusted");
    }

    internal static void ThrowIfPackageRejected(
        ManagedModuleRepository repository,
        ManagedModulePackageMetadata? metadata,
        ManagedModuleTrustPolicy? policy)
    {
        var allowedAuthors = NormalizeAuthors(policy?.AllowedAuthors);
        if (allowedAuthors.Count == 0)
            return;

        var packageAuthors = SplitAuthors(metadata?.Authors).ToArray();
        if (packageAuthors.Length == 0)
            throw PackageException(repository, metadata, "PackageAuthorsMissing", "does not declare package authors");

        if (packageAuthors.Any(author => allowedAuthors.Contains(author, StringComparer.OrdinalIgnoreCase)))
            return;

        throw PackageException(
            repository,
            metadata,
            "PackageAuthorNotAllowed",
            $"declares author(s) '{string.Join(", ", packageAuthors)}', which do not match the allowed author policy");
    }

    private static ManagedModuleTrustException PackageException(
        ManagedModuleRepository repository,
        ManagedModulePackageMetadata? metadata,
        string reason,
        string detail)
    {
        var name = string.IsNullOrWhiteSpace(metadata?.Id) ? "package" : $"package '{metadata!.Id}'";
        var version = string.IsNullOrWhiteSpace(metadata?.Version) ? string.Empty : $" {metadata!.Version}";
        return new ManagedModuleTrustException(
            $"Managed module trust policy rejected {name}{version}: {detail}.",
            metadata?.Id,
            metadata?.Version,
            repository.Name,
            reason);
    }

    private static IEnumerable<string> SplitAuthors(string? authors)
    {
        if (string.IsNullOrWhiteSpace(authors))
            yield break;

        foreach (var author in authors!.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = author.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }
}
