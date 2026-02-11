using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml.Linq;
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
        static PublishGitHubReleaseAssetResult NewResult() => new()
        {
            Success = false,
            TagName = null,
            ReleaseName = null,
            ZipPath = null,
            ReleaseUrl = null,
            ErrorMessage = null
        };

        var versionOverride = Version;
        var tagNameOverride = TagName;
        var releaseNameOverride = ReleaseName;
        var zipOverride = ZipPath;
        var tagTemplate = TagTemplate;
        var includeProjectInTag = IncludeProjectNameInTag.IsPresent;
        var isPreRelease = IsPreRelease.IsPresent;
        var generateReleaseNotes = GenerateReleaseNotes.IsPresent;

        var projectPaths = ProjectPath ?? Array.Empty<string>();
        if (projectPaths.Length > 1 && !string.IsNullOrWhiteSpace(zipOverride))
        {
            var r = NewResult();
            r.ErrorMessage = "ZipPath override is not supported when multiple ProjectPath values are provided.";
            WriteObject(r);
            return;
        }

        var entries = new List<(string ProjectName, string TagName, string ReleaseName, string ZipPath)>();

        try
        {
            foreach (var projectPath in projectPaths)
            {
                var result = NewResult();

                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    result.ErrorMessage = "ProjectPath contains an empty value.";
                    WriteObject(result);
                    continue;
                }

                string fullProjectPath;
                try { fullProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(projectPath); }
                catch { fullProjectPath = projectPath; }

                if (!Directory.Exists(fullProjectPath) && !File.Exists(fullProjectPath))
                {
                    result.ErrorMessage = $"Project path '{projectPath}' not found.";
                    WriteObject(result);
                    continue;
                }

                var csproj = ResolveCsproj(fullProjectPath);
                if (csproj is null)
                {
                    result.ErrorMessage = $"No csproj found in {projectPath}";
                    WriteObject(result);
                    continue;
                }

                var projectName = Path.GetFileNameWithoutExtension(csproj) ?? "Project";
                var csprojDir = Path.GetDirectoryName(csproj) ?? fullProjectPath;

                var version = versionOverride;
                if (string.IsNullOrWhiteSpace(version))
                {
                    var doc = XDocument.Load(csproj);
                    version = GetFirstElementValue(doc, "VersionPrefix");
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        result.ErrorMessage = $"VersionPrefix not found in '{csproj}'";
                        WriteObject(result);
                        continue;
                    }
                }

                var zip = string.IsNullOrWhiteSpace(zipOverride)
                    ? Path.Combine(csprojDir, "bin", "Release", $"{projectName}.{version}.zip")
                    : SessionState.Path.GetUnresolvedProviderPathFromPSPath(zipOverride);

                if (!File.Exists(zip))
                {
                    result.ErrorMessage = $"Zip file '{zip}' not found.";
                    WriteObject(result);
                    continue;
                }

                var tag = tagNameOverride;
                if (string.IsNullOrWhiteSpace(tag))
                {
                    if (!string.IsNullOrWhiteSpace(tagTemplate))
                    {
                        tag = tagTemplate!.Replace("{Project}", projectName).Replace("{Version}", version);
                    }
                    else if (includeProjectInTag)
                    {
                        tag = $"{projectName}-v{version}";
                    }
                    else
                    {
                        tag = $"v{version}";
                    }
                }

                var relName = !string.IsNullOrWhiteSpace(releaseNameOverride) ? releaseNameOverride : tag;

                entries.Add((projectName, tag!, relName!, zip));
            }

            if (entries.Count == 0)
                return;

            var sb = ScriptBlock.Create(PowerForgeScripts.Load("Scripts/Cmdlets/Invoke-SendGitHubRelease.ps1"));

            foreach (var group in entries.GroupBy(e => e.TagName, StringComparer.OrdinalIgnoreCase))
            {
                var tag = group.Key;
                var relName = group.First().ReleaseName;
                var assets = group.Select(e => e.ZipPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                bool succeeded;
                string? releaseUrl;
                string? errorMessage;

                if (!ShouldProcess($"{GitHubUsername}/{GitHubRepositoryName}", $"Publish release {tag} to GitHub"))
                {
                    succeeded = true;
                    errorMessage = null;
                    releaseUrl = $"https://github.com/{GitHubUsername}/{GitHubRepositoryName}/releases/tag/{tag}";
                }
                else
                {
                    // ModuleInfo.NewBoundScriptBlock works only for script modules. PSPublishModule cmdlets execute
                    // in the binary module context, so we must invoke directly.
                    var output = sb.Invoke(
                        GitHubUsername,
                        GitHubRepositoryName,
                        GitHubAccessToken,
                        tag,
                        relName,
                        assets,
                        isPreRelease,
                        generateReleaseNotes,
                        true);
                    var status = output.Count > 0 ? output[0]?.BaseObject : null;

                    succeeded = false;
                    releaseUrl = null;
                    errorMessage = null;

                    if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
                    {
                        succeeded = gr.Succeeded;
                        releaseUrl = gr.ReleaseUrl;
                        errorMessage = gr.Succeeded ? null : gr.ErrorMessage;
                    }
                    else if (status is PSObject pso)
                    {
                        var ok = pso.Properties["Succeeded"]?.Value as bool?;
                        succeeded = ok ?? false;
                        releaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                        errorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
                    }
                    else
                    {
                        errorMessage = "Unexpected result from Send-GitHubRelease.";
                    }
                }

                foreach (var e in group)
                {
                    var r = NewResult();
                    r.Success = succeeded;
                    r.TagName = tag;
                    r.ReleaseName = relName;
                    r.ZipPath = e.ZipPath;
                    r.ReleaseUrl = releaseUrl;
                    r.ErrorMessage = succeeded ? null : errorMessage;
                    WriteObject(r);
                }
            }
        }
        catch (Exception ex)
        {
            var r = NewResult();
            r.Success = false;
            r.ErrorMessage = ex.Message;
            WriteObject(r);
        }
    }

    private static string? ResolveCsproj(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return null;

        return Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? GetFirstElementValue(XDocument doc, string localName)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

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
