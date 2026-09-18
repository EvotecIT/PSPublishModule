namespace PowerForge;

/// <summary>
/// Internal build-plan binding for an approved module donor.
/// </summary>
internal sealed class ApprovedModuleResolution
{
    internal string Name { get; }
    internal RequiredModuleReference Constraint { get; }
    internal ModuleDependencyVersionSource VersionSource { get; }
    internal string? Repository { get; }
    internal RepositoryCredential? Credential { get; }
    internal bool Prerelease { get; }
    internal bool MatchPrereleaseByBaseVersion { get; }

    internal ApprovedModuleResolution(
        string name,
        RequiredModuleReference constraint,
        ModuleDependencyVersionSource versionSource,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease,
        bool matchPrereleaseByBaseVersion)
    {
        Name = name;
        Constraint = constraint;
        VersionSource = versionSource;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
        Credential = credential;
        Prerelease = prerelease;
        MatchPrereleaseByBaseVersion = matchPrereleaseByBaseVersion;
    }
}
