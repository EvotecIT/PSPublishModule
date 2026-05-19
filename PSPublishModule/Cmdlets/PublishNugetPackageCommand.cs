using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Pushes NuGet packages to a feed using <c>dotnet nuget push</c>.
/// </summary>
/// <remarks>
/// <para>
/// Searches the provided <c>-Path</c> roots for <c>*.nupkg</c> files and pushes them using the .NET SDK.
/// </para>
/// <para>
/// Use <c>-SkipDuplicate</c> for CI-friendly, idempotent runs.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish all packages from a Release folder</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Publish-NugetPackage -Path '.\bin\Release' -ApiKey $env:NUGET_API_KEY -SkipDuplicate</code>
/// <para>Publishes all .nupkg files under the folder; safe to rerun in CI.</para>
/// </example>
/// <example>
/// <summary>Publish to a custom feed</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Publish-NugetPackage -Path '.\artifacts' -ApiKey 'YOUR_KEY' -Source 'https://api.nuget.org/v3/index.json'</code>
/// <para>Use a different source URL for private feeds (e.g. GitHub Packages, Azure Artifacts).</para>
/// </example>
/// <example>
/// <summary>Publish to a saved Azure Artifacts profile</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Publish-NugetPackage -Path '.\artifacts' -ProfileName 'Company' -SkipDuplicate</code>
/// <para>Resolves the Azure Artifacts NuGet v3 source from the saved profile and lets the Azure Artifacts Credential Provider handle Entra-backed authentication.</para>
/// </example>
[Cmdlet(VerbsData.Publish, "NugetPackage", DefaultParameterSetName = ParameterSetSource, SupportsShouldProcess = true)]
public sealed class PublishNugetPackageCommand : PSCmdlet
{
    private const string ParameterSetSource = "Source";
    private const string ParameterSetProfile = "Profile";
    private const string AzureArtifactsApiKeyPlaceholder = "AzureArtifacts";

    /// <summary>Directory to search for NuGet packages.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetSource)]
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [ValidateNotNullOrEmpty]
    public string[] Path { get; set; } = Array.Empty<string>();

    /// <summary>API key used to authenticate against the NuGet feed. For Azure Artifacts profiles this defaults to a non-secret placeholder used by NuGet clients.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetSource)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>NuGet feed URL.</summary>
    [Parameter(ParameterSetName = ParameterSetSource)]
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>Saved private gallery profile name for Azure Artifacts package publishing.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// When set, passes <c>--skip-duplicate</c> to <c>dotnet nuget push</c>.
    /// This makes repeated publishing runs idempotent when the package already exists.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipDuplicate { get; set; }

    /// <summary>Executes publishing.</summary>
    protected override void ProcessRecord()
    {
        try
        {
            var source = Source;
            var apiKey = ApiKey;
            string? profileName = null;
            string? repositoryName = null;

            if (ParameterSetName == ParameterSetProfile)
            {
                var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName);
                var endpoint = AzureArtifactsRepositoryEndpoints.Create(
                    profile.AzureDevOpsOrganization,
                    profile.AzureDevOpsProject,
                    profile.AzureArtifactsFeed,
                    profile.RepositoryName);

                source = endpoint.PSResourceGetUri;
                repositoryName = endpoint.RepositoryName;
                profileName = profile.Name;
                if (string.IsNullOrWhiteSpace(apiKey))
                    apiKey = AzureArtifactsApiKeyPlaceholder;
            }

            var logger = new CmdletLogger(this, MyInvocation?.BoundParameters.ContainsKey("Verbose") == true);
            var service = new NuGetPackagePublishService(logger);
            var resolvedRoots = ResolveRoots(Path);
            var serviceResult = service.Execute(new NuGetPackagePublishRequest
            {
                Roots = resolvedRoots,
                ApiKey = apiKey,
                Source = source,
                SkipDuplicate = SkipDuplicate.IsPresent
            }, package => ShouldProcess(package, $"Publish NuGet package to {source}"));

            WriteObject(ToCmdletResult(serviceResult, source, profileName, repositoryName));
        }
        catch (Exception ex)
        {
            WriteObject(new PublishNugetPackageResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private string[] ResolveRoots(IEnumerable<string>? rawPaths)
        => (rawPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path!))
        .ToArray();

    private static PublishNugetPackageResult ToCmdletResult(
        PowerForge.NuGetPackagePublishResult result,
        string source,
        string? profileName,
        string? repositoryName)
    {
        var mapped = new PublishNugetPackageResult
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            Source = source,
            ProfileName = profileName,
            RepositoryName = repositoryName
        };

        mapped.Pushed.AddRange(result.PublishedItems);
        mapped.Failed.AddRange(result.FailedItems);
        return mapped;
    }

    /// <summary>Result returned by <c>Publish-NugetPackage</c>.</summary>
    public sealed class PublishNugetPackageResult
    {
        /// <summary>Whether all packages were pushed successfully.</summary>
        public bool Success { get; set; } = true;

        /// <summary>List of packages pushed (or simulated in WhatIf).</summary>
        public List<string> Pushed { get; } = new();

        /// <summary>List of packages that failed to push.</summary>
        public List<string> Failed { get; } = new();

        /// <summary>Optional error message for overall failure (e.g., path not found).</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>NuGet feed URL used for the push operation.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Saved profile name used to resolve the feed, when applicable.</summary>
        public string? ProfileName { get; set; }

        /// <summary>Local repository name resolved from the saved profile, when applicable.</summary>
        public string? RepositoryName { get; set; }
    }
}
