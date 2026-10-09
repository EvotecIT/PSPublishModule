namespace PowerForge;

/// <summary>Shared dependency policy and endpoint selection for module publishing.</summary>
internal static class ModulePublishDependencyPolicy
{
    internal static void Validate(PublishConfiguration publish, PublishTool tool)
    {
        if (publish.SkipDependenciesCheck && publish.PublishRequiredModules)
            throw new InvalidOperationException("SkipDependenciesCheck cannot be combined with PublishRequiredModules. Choose availability validation and mirroring, or skip the availability check.");
        if (publish.SkipDependenciesCheck && tool == PublishTool.PowerShellGet)
            throw new NotSupportedException("PowerShellGet does not support skipping publish dependency checks. Use Tool = PSResourceGet or ManagedModule with SkipDependenciesCheck.");
        if (publish.PublishRequiredModules && tool == PublishTool.PowerShellGet)
            throw new InvalidOperationException("PublishRequiredModules requires PSResourceGet or ManagedModule because dependency mirroring saves and republishes dependency graphs before publishing the main module. Select a supported tool or disable PublishRequiredModules.");
    }

    internal static string? ReadUri(PublishRepositoryConfiguration? repository)
        => HasLegacyProtocolEndpoints(repository)
            ? FirstNonEmpty(repository?.Uri, repository?.SourceUri, repository?.PublishUri)
            : FirstNonEmpty(repository?.SourceUri, repository?.Uri, repository?.PublishUri);

    internal static string? PublishUri(PublishRepositoryConfiguration? repository)
        => FirstNonEmpty(repository?.PublishUri, repository?.Uri, repository?.SourceUri);

    // Preserve the single-endpoint registration contract used by provider presets.
    // For a split feed, PSResourceGet registers only the upload endpoint; reads use the managed client.
    internal static string? PSResourceGetRegistrationUri(PublishRepositoryConfiguration repository)
        => HasSeparateEndpoints(repository)
            ? PublishUri(repository)
            : FirstNonEmpty(repository.Uri, repository.PublishUri, repository.SourceUri);

    internal static bool HasSeparateEndpoints(PublishRepositoryConfiguration? repository)
    {
        if (HasLegacyProtocolEndpoints(repository))
            return false;
        var read = ReadUri(repository);
        var publish = PublishUri(repository);
        return read is not null && publish is not null &&
               !string.Equals(read.TrimEnd('/'), publish.TrimEnd('/'), StringComparison.Ordinal);
    }

    // Provider presets carry one feed as a v3 Uri and equal legacy source/publish URLs.
    private static bool HasLegacyProtocolEndpoints(PublishRepositoryConfiguration? repository)
        => !string.IsNullOrWhiteSpace(repository?.SourceUri) &&
           !string.IsNullOrWhiteSpace(repository?.PublishUri) &&
           string.Equals(repository!.SourceUri!.Trim().TrimEnd('/'),
               repository.PublishUri!.Trim().TrimEnd('/'), StringComparison.Ordinal);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
