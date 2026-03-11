using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Publishes a release asset to GitHub (creates a release and uploads a zip).
/// </summary>
/// <remarks>
/// <para>
/// Reads project metadata from <c>*.csproj</c>, resolves the release version (unless overridden),
/// creates a GitHub release, and uploads the specified ZIP asset.
/// </para>
/// <para>
/// For private repositories, use a token with the minimal required scope and prefer providing it via an environment variable.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a release and upload the default ZIP asset</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Publish-GitHubReleaseAsset -ProjectPath '.\MyProject\MyProject.csproj' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN</code>
/// <para>Creates a GitHub release and uploads <c>bin\Release\&lt;Project&gt;.&lt;Version&gt;.zip</c>.</para>
/// </example>
/// <example>
/// <summary>Publish a pre-release with a custom tag template</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Publish-GitHubReleaseAsset -ProjectPath '.\MyProject\MyProject.csproj' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -IsPreRelease -TagTemplate '{Project}-v{Version}'</code>
/// <para>Useful when your repository uses a specific tag naming convention.</para>
/// </example>
[Cmdlet(VerbsData.Publish, "GitHubReleaseAsset", SupportsShouldProcess = true)]
public sealed class PublishGitHubReleaseAssetCommand : PSCmdlet
{
    /// <summary>Path to the project folder containing the *.csproj file.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] ProjectPath { get; set; } = Array.Empty<string>();

    /// <summary>GitHub account name owning the repository.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubUsername { get; set; } = string.Empty;

    /// <summary>Name of the GitHub repository.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubRepositoryName { get; set; } = string.Empty;

    /// <summary>Personal access token used for authentication.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubAccessToken { get; set; } = string.Empty;

    /// <summary>Publish the release as a pre-release.</summary>
    [Parameter]
    public SwitchParameter IsPreRelease { get; set; }

    /// <summary>When set, asks GitHub to generate release notes automatically.</summary>
    [Parameter]
    public SwitchParameter GenerateReleaseNotes { get; set; }

    /// <summary>Optional version override (otherwise read from VersionPrefix).</summary>
    [Parameter]
    public string? Version { get; set; }

    /// <summary>Optional tag name override.</summary>
    [Parameter]
    public string? TagName { get; set; }

    /// <summary>Optional tag template (supports {Project} and {Version}).</summary>
    [Parameter]
    public string? TagTemplate { get; set; }

    /// <summary>Optional release name override (defaults to TagName).</summary>
    [Parameter]
    public string? ReleaseName { get; set; }

    /// <summary>When set, generates tag name as &lt;Project&gt;-v&lt;Version&gt;.</summary>
    [Parameter]
    public SwitchParameter IncludeProjectNameInTag { get; set; }

    /// <summary>Optional zip path override (defaults to bin/Release/&lt;Project&gt;.&lt;Version&gt;.zip).</summary>
    [Parameter]
    public string? ZipPath { get; set; }

    /// <summary>Publishes the release asset.</summary>
    protected override void ProcessRecord()
    {
        try
        {
            var isVerbose = MyInvocation?.BoundParameters.ContainsKey("Verbose") == true;
            var logger = new CmdletLogger(this, isVerbose);
            var workflow = new GitHubReleaseAssetWorkflowService(logger);
            var shouldPublish = ShouldProcess($"{GitHubUsername}/{GitHubRepositoryName}", "Publish GitHub release assets");
            var request = new GitHubReleaseAssetWorkflowRequest
            {
                ProjectPaths = ResolveProjectPaths(ProjectPath),
                Owner = GitHubUsername,
                Repository = GitHubRepositoryName,
                Token = GitHubAccessToken,
                IsPreRelease = IsPreRelease.IsPresent,
                GenerateReleaseNotes = GenerateReleaseNotes.IsPresent,
                Version = Version,
                TagName = TagName,
                TagTemplate = TagTemplate,
                ReleaseName = ReleaseName,
                IncludeProjectNameInTag = IncludeProjectNameInTag.IsPresent,
                ZipPath = ResolveOptionalPath(ZipPath)
            };

            foreach (var result in workflow.Execute(request, shouldPublish))
                WriteObject(ToCmdletResult(result));
        }
        catch (Exception ex)
        {
            WriteObject(new PublishGitHubReleaseAssetResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private string[] ResolveProjectPaths(IEnumerable<string>? projectPaths)
    {
        return (projectPaths ?? Array.Empty<string>())
            .Select(path => ResolvePathWithFallback(path ?? string.Empty))
            .ToArray();
    }

    private string? ResolveOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : ResolvePathWithFallback(path!);

    private string ResolvePathWithFallback(string path)
    {
        try { return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path); }
        catch { return path; }
    }

    private static PublishGitHubReleaseAssetResult ToCmdletResult(GitHubReleaseAssetWorkflowResult result)
        => new()
        {
            Success = result.Success,
            TagName = result.TagName,
            ReleaseName = result.ReleaseName,
            ZipPath = result.ZipPath,
            ReleaseUrl = result.ReleaseUrl,
            ErrorMessage = result.ErrorMessage
        };

    /// <summary>Result returned by <c>Publish-GitHubReleaseAsset</c>.</summary>
    public sealed class PublishGitHubReleaseAssetResult
    {
        /// <summary>True if publishing succeeded (or simulated in WhatIf).</summary>
        public bool Success { get; set; }

        /// <summary>Computed tag name used for the release.</summary>
        public string? TagName { get; set; }

        /// <summary>Computed release name.</summary>
        public string? ReleaseName { get; set; }

        /// <summary>Zip file path used as the asset.</summary>
        public string? ZipPath { get; set; }

        /// <summary>URL of the created release.</summary>
        public string? ReleaseUrl { get; set; }

        /// <summary>Error message when <see cref="Success"/> is false.</summary>
        public string? ErrorMessage { get; set; }
    }
}
