namespace PowerForge;

/// <summary>Internal source policy attached to one declared module dependency.</summary>
internal sealed class ModuleDependencySourceResolution
{
    internal string Name { get; }
    internal ModuleDependencyVersionSource VersionSource { get; }
    internal string? Repository { get; }
    internal RepositoryCredential? Credential { get; }
    internal string? RequiredVersion { get; }
    internal string? MinimumVersion { get; }

    internal ModuleDependencySourceResolution(
        string name,
        ModuleDependencyVersionSource versionSource,
        string? repository,
        RepositoryCredential? credential,
        string? requiredVersion,
        string? minimumVersion)
    {
        Name = name;
        VersionSource = versionSource;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
        Credential = credential;
        RequiredVersion = string.IsNullOrWhiteSpace(requiredVersion) ? null : requiredVersion!.Trim();
        MinimumVersion = string.IsNullOrWhiteSpace(minimumVersion) ? null : minimumVersion!.Trim();
    }

    internal bool Matches(ModuleDependency dependency)
        => dependency is not null &&
           string.Equals(Name, dependency.Name, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(RequiredVersion, dependency.RequiredVersion, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(MinimumVersion, dependency.MinimumVersion, StringComparison.OrdinalIgnoreCase);
}
