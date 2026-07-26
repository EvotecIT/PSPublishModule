using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Finds module versions from a managed module repository.
/// </summary>
/// <remarks>
/// <para>
/// This command is the module-package equivalent of <c>Find-PSResource</c>. It queries NuGet-compatible or
/// local-folder repositories through the managed C# repository client and returns typed module version objects
/// that can be piped to <c>Install-ManagedModule</c> or <c>Save-ManagedModule</c>.
/// </para>
/// <para>
/// Name, exact/range version, prerelease, tag, and dependency searches are supported for module packages.
/// Command-name and DSC-resource-name searches inspect package contents and remain outside this fast metadata path.
/// </para>
/// </remarks>
/// <example>
/// <summary>Find the latest stable version of a module</summary>
/// <code>Find-ManagedModule -Name Company.Tools</code>
/// </example>
/// <example>
/// <summary>Find modules using a wildcard package id</summary>
/// <code>Find-ManagedModule -Name Company.* -Repository C:\Packages</code>
/// </example>
/// <example>
/// <summary>Find all versions from a local folder feed</summary>
/// <code>Find-ManagedModule -Name Company.Tools -Repository C:\Packages -AllVersions -AllowPrerelease</code>
/// </example>
/// <example>
/// <summary>Find versions in a NuGet range and pipe the typed results to save</summary>
/// <code>Find-ManagedModule -Name Company.Tools -Version '[1.2.0,2.0.0)' -ProfileName CompanyModules | Save-ManagedModule -Path C:\OfflineModules</code>
/// </example>
[Cmdlet(VerbsCommon.Find, "ManagedModule")]
[OutputType(typeof(ManagedModuleVersionInfo))]
public sealed class FindManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names or wildcard patterns to find. When omitted, all module package names are considered.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = new[] { "*" };

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Saved module repository profile to use instead of Repository.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

    /// <summary>Exact version, wildcard version, or NuGet-style version range to return.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Return all matching versions instead of only the latest selected version.</summary>
    [Parameter]
    public SwitchParameter AllVersions { get; set; }

    /// <summary>Maximum search results returned for wildcard name queries.</summary>
    [Parameter]
    [ValidateRange(1, 1000)]
    public int First { get; set; } = 100;

    /// <summary>Filter results by package tag metadata.</summary>
    [Parameter]
    [Alias("Tags")]
    [ValidateNotNullOrEmpty]
    public string[]? Tag { get; set; }

    /// <summary>Resource kind to find. Find-ManagedModule currently returns module resources.</summary>
    [Parameter]
    [Alias("Type")]
    [ValidateSet("Module")]
    public string[]? ResourceType { get; set; }

    /// <summary>Include dependency resources exposed by repository metadata.</summary>
    [Parameter]
    public SwitchParameter IncludeDependencies { get; set; }

    /// <summary>Include prerelease versions.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Optional repository credential.</summary>
    [Parameter]
    public PSCredential? Credential { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Optional HTTP proxy used for repository requests.</summary>
    [Parameter]
    [ValidateNotNull]
    public Uri? Proxy { get; set; }

    /// <summary>Optional proxy credential used with Proxy.</summary>
    [Parameter]
    public PSCredential? ProxyCredential { get; set; }

    /// <summary>Finds matching module versions.</summary>
    protected override void ProcessRecord()
    {
        var repository = ManagedModuleCommandSupport.CreateRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey(nameof(Repository)));
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var client = ManagedModuleCommandSupport.CreateRepositoryClient(this, logger, Proxy, ProxyCredential);

        var roots = IncludeDependencies.IsPresent
            ? new List<ManagedModuleVersionInfo>()
            : null;
        foreach (var moduleName in Name)
        {
            var output = ManagedModuleCommandSupport.HasWildcard(moduleName)
                ? FindWildcardPackageVersions(client, repository, moduleName, credential)
                : FindExactPackageVersions(client, repository, moduleName, credential);
            output = HydrateVersionsForFindAsync(client, repository, output, credential)
                .GetAwaiter()
                .GetResult();
            output = ApplyFindFilters(output, moduleName);
            if (roots is not null)
            {
                roots.AddRange(output);
                continue;
            }

            foreach (var version in output)
                WriteVersion(version);
        }

        if (roots is null)
            return;

        var results = roots.Count > 0
            ? IncludeDependencyVersions(client, repository, roots, credential)
            : roots;
        foreach (var version in results)
            WriteVersion(version);
    }

    private void WriteVersion(ManagedModuleVersionInfo version)
    {
        version.RepositoryProfileName = string.IsNullOrWhiteSpace(ProfileName) ? null : ProfileName!.Trim();
        WriteObject(version);
    }

    private IReadOnlyList<ManagedModuleVersionInfo> FindExactPackageVersions(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        string moduleName,
        RepositoryCredential? credential)
    {
        if (AllVersions.IsPresent || HasVersionFilter())
        {
            return client.GetVersionsAsync(repository, moduleName, ShouldIncludePrerelease(), credential)
                .GetAwaiter()
                .GetResult();
        }

        var latest = client.GetLatestVersionAsync(repository, moduleName, ShouldIncludePrerelease(), credential)
            .GetAwaiter()
            .GetResult();
        return latest is null ? Array.Empty<ManagedModuleVersionInfo>() : new[] { latest };
    }

    private IReadOnlyList<ManagedModuleVersionInfo> FindWildcardPackageVersions(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        string moduleName,
        RepositoryCredential? credential)
    {
        if (HasTagFilters() || HasVersionFilter())
            return FindWildcardPackageVersionsWithFilterPaging(client, repository, moduleName, credential);

        var searchQuery = ResolveSearchQuery(moduleName);
        var matches = client.SearchPackagesAsync(repository, searchQuery, ShouldIncludePrerelease(), credential, First)
            .GetAwaiter()
            .GetResult();
        if ((!AllVersions.IsPresent && !HasVersionFilter()) || matches.Count == 0)
            return matches;

        var versions = new List<ManagedModuleVersionInfo>();
        foreach (var match in matches)
        {
            versions.AddRange(client.GetVersionsAsync(repository, match.Name, ShouldIncludePrerelease(), credential)
                .GetAwaiter()
                .GetResult());
        }

        return versions
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private IReadOnlyList<ManagedModuleVersionInfo> FindWildcardPackageVersionsWithFilterPaging(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        string moduleName,
        RepositoryCredential? credential)
    {
        const int pageSize = 1000;
        var searchQuery = ResolveSearchQuery(moduleName);
        var results = new List<ManagedModuleVersionInfo>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var skip = 0; !HasEnoughPagedMatches(results, seenPackages);)
        {
            var page = client.SearchPackagesAsync(repository, searchQuery, ShouldIncludePrerelease(), credential, pageSize, skip)
                .GetAwaiter()
                .GetResult();
            if (page.Count == 0)
                break;

            skip += page.Count;

            var candidates = AllVersions.IsPresent || HasVersionFilter()
                ? ExpandAllVersions(client, repository, page, credential)
                : page;
            candidates = HydrateVersionsForFindAsync(client, repository, candidates, credential)
                .GetAwaiter()
                .GetResult();
            candidates = ApplyFindFilters(candidates, moduleName, applyFirst: false);
            foreach (var candidate in candidates)
            {
                var isNewPackage = !seenPackages.Contains(candidate.Name);
                if (AllVersions.IsPresent && isNewPackage && seenPackages.Count >= First)
                {
                    break;
                }

                if (!seenVersions.Add(CreateResultKey(candidate)))
                    continue;

                seenPackages.Add(candidate.Name);
                results.Add(candidate);
                if (!AllVersions.IsPresent && HasEnoughPagedMatches(results, seenPackages))
                    break;
            }
        }

        var ordered = results
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
        return AllVersions.IsPresent ? ordered : ordered.Take(First).ToArray();
    }

    private bool HasEnoughPagedMatches(
        IReadOnlyCollection<ManagedModuleVersionInfo> results,
        IReadOnlyCollection<string> packageNames)
        => AllVersions.IsPresent ? packageNames.Count >= First : results.Count >= First;

    private IReadOnlyList<ManagedModuleVersionInfo> ExpandAllVersions(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleVersionInfo> matches,
        RepositoryCredential? credential)
    {
        var versions = new List<ManagedModuleVersionInfo>();
        foreach (var match in matches)
        {
            versions.AddRange(client.GetVersionsAsync(repository, match.Name, ShouldIncludePrerelease(), credential)
                .GetAwaiter()
                .GetResult());
        }

        return versions
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private IReadOnlyList<ManagedModuleVersionInfo> ApplyFindFilters(
        IReadOnlyList<ManagedModuleVersionInfo> output,
        string moduleName,
        bool applyFirst = true)
    {
        var hasWildcard = ManagedModuleCommandSupport.HasWildcard(moduleName);
        var filtered = output.Where(version => MatchesRequestedName(moduleName, version.Name, hasWildcard));

        var tagFilters = GetTagFilters();
        if (tagFilters is { Length: > 0 })
        {
            filtered = filtered.Where(version =>
                version.Tags.Count > 0 &&
                tagFilters.All(tag => version.Tags.Any(packageTag => TagsMatch(tag, packageTag))));
        }

        filtered = filtered.Where(version => ManagedModuleVersionSelector.IsSelectable(version, Version));

        if (applyFirst && hasWildcard && !AllVersions.IsPresent)
            filtered = filtered.Take(First);

        return filtered.ToArray();
    }

    private string ResolveSearchQuery(string moduleName)
    {
        if (!IsBroadWildcard(moduleName))
            return moduleName;

        return moduleName;
    }

    private IReadOnlyList<ManagedModuleVersionInfo> IncludeDependencyVersions(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleVersionInfo> roots,
        RepositoryCredential? credential)
    {
        var results = new List<ManagedModuleVersionInfo>();
        var queue = new Queue<ManagedModuleVersionInfo>(roots);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(CreateResultKey(current)))
                continue;

            results.Add(current);
            foreach (var dependency in current.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.Id))
                    continue;

                var dependencyVersion = client.GetLatestDependencyVersionAsync(
                        repository,
                        dependency,
                        ShouldIncludePrerelease(),
                        credential)
                    .GetAwaiter()
                    .GetResult();
                if (dependencyVersion is not null)
                {
                    var hydratedDependency = HydrateVersionForFindAsync(client, repository, dependencyVersion, credential)
                        .GetAwaiter()
                        .GetResult();
                    if (!seen.Contains(CreateResultKey(hydratedDependency)))
                        queue.Enqueue(hydratedDependency);
                }
            }
        }

        return results
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private static string CreateResultKey(ManagedModuleVersionInfo version)
        => string.Join("|", version.ResourceType, version.Name, version.Version);

    private bool HasTagFilters()
        => Tag?.Any(static tag => !string.IsNullOrWhiteSpace(tag)) == true;

    private bool HasVersionFilter()
        => !string.IsNullOrWhiteSpace(Version);

    private bool ShouldIncludePrerelease()
        => Prerelease.IsPresent || ManagedModuleVersionSelector.IncludesPrerelease(Version);

    private string[] GetTagFilters()
        => Tag?
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .ToArray() ?? Array.Empty<string>();

    private bool RequiresMetadataHydration(ManagedModuleVersionInfo version)
    {
        if (HasTagFilters() && version.Tags.Count == 0)
            return true;

        return IncludeDependencies.IsPresent && version.Dependencies.Count == 0;
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<ManagedModuleVersionInfo>> HydrateVersionsForFindAsync(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        IReadOnlyList<ManagedModuleVersionInfo> versions,
        RepositoryCredential? credential)
    {
        if (!HasTagFilters() && !IncludeDependencies.IsPresent)
            return versions;

        var hydrated = new List<ManagedModuleVersionInfo>(versions.Count);
        foreach (var version in versions)
            hydrated.Add(await HydrateVersionForFindAsync(client, repository, version, credential).ConfigureAwait(false));

        return hydrated;
    }

    private async System.Threading.Tasks.Task<ManagedModuleVersionInfo> HydrateVersionForFindAsync(
        ManagedModuleRepositoryClient client,
        ManagedModuleRepository repository,
        ManagedModuleVersionInfo version,
        RepositoryCredential? credential)
    {
        if (!RequiresMetadataHydration(version))
            return version;

        var metadata = await client.GetPackageMetadataAsync(
                repository,
                version.Name,
                version.Version,
                credential,
                System.Threading.CancellationToken.None)
            .ConfigureAwait(false);
        return metadata is null ? version : CopyVersionInfoWithPackageMetadata(version, metadata);
    }

    internal static ManagedModuleVersionInfo CopyVersionInfoWithPackageMetadata(
        ManagedModuleVersionInfo version,
        ManagedModulePackageMetadata metadata)
        => new()
        {
            Name = version.Name,
            Version = version.Version,
            RepositoryName = version.RepositoryName,
            RepositorySource = version.RepositorySource,
            RepositoryProfileName = version.RepositoryProfileName,
            PackageSource = version.PackageSource,
            IsPrerelease = version.IsPrerelease,
            Listed = version.Listed,
            License = metadata.License ?? version.License,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
            Dependencies = metadata.Dependencies,
            Tags = metadata.Tags,
            ResourceType = version.ResourceType
        };

    private static bool MatchesRequestedName(string requestedName, string packageName, bool hasWildcard)
        => hasWildcard
            ? ManagedModuleSearchMatcher.IsMatch(requestedName, packageName)
            : packageName.Equals(requestedName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool TagsMatch(string requestedTag, string packageTag)
        => packageTag.Equals(requestedTag.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsBroadWildcard(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ||
               trimmed.Equals("*", StringComparison.Ordinal) ||
               trimmed.Equals("?", StringComparison.Ordinal);
    }
}
