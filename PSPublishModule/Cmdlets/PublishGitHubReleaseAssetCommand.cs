using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml.Linq;

namespace PSPublishModule;

/// <summary>
/// Publishes a release asset to GitHub (creates a release and uploads a zip).
/// </summary>
[Cmdlet(VerbsData.Publish, "GitHubReleaseAsset", SupportsShouldProcess = true)]
public sealed class PublishGitHubReleaseAssetCommand : PSCmdlet
{
    /// <summary>Path to the project folder containing the *.csproj file.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

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
        var result = new PublishGitHubReleaseAssetResult
        {
            Success = false,
            TagName = null,
            ReleaseName = null,
            ZipPath = null,
            ReleaseUrl = null,
            ErrorMessage = null
        };

        try
        {
            var fullProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
            if (!Directory.Exists(fullProjectPath) && !File.Exists(fullProjectPath))
            {
                result.ErrorMessage = $"Project path '{ProjectPath}' not found.";
                WriteObject(result);
                return;
            }

            var csproj = ResolveCsproj(fullProjectPath);
            if (csproj is null)
            {
                result.ErrorMessage = $"No csproj found in {ProjectPath}";
                WriteObject(result);
                return;
            }

            var projectName = Path.GetFileNameWithoutExtension(csproj) ?? "Project";
            var csprojDir = Path.GetDirectoryName(csproj) ?? fullProjectPath;

            var doc = XDocument.Load(csproj);
            if (string.IsNullOrWhiteSpace(Version))
            {
                Version = GetFirstElementValue(doc, "VersionPrefix");
                if (string.IsNullOrWhiteSpace(Version))
                {
                    result.ErrorMessage = $"VersionPrefix not found in '{csproj}'";
                    WriteObject(result);
                    return;
                }
            }

            var zip = string.IsNullOrWhiteSpace(ZipPath)
                ? Path.Combine(csprojDir, "bin", "Release", $"{projectName}.{Version}.zip")
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ZipPath);

            if (!File.Exists(zip))
            {
                result.ErrorMessage = $"Zip file '{zip}' not found.";
                WriteObject(result);
                return;
            }

            if (string.IsNullOrWhiteSpace(TagName))
            {
                if (TagTemplate is not null && !string.IsNullOrWhiteSpace(TagTemplate))
                {
                    TagName = TagTemplate.Replace("{Project}", projectName).Replace("{Version}", Version);
                }
                else if (IncludeProjectNameInTag.IsPresent)
                {
                    TagName = $"{projectName}-v{Version}";
                }
                else
                {
                    TagName = $"v{Version}";
                }
            }

            result.TagName = TagName;
            if (string.IsNullOrWhiteSpace(ReleaseName))
            {
                ReleaseName = TagName;
            }
            result.ReleaseName = ReleaseName;
            result.ZipPath = zip;

            if (!ShouldProcess($"{GitHubUsername}/{GitHubRepositoryName}", $"Publish release {TagName} to GitHub"))
            {
                result.Success = true;
                result.ReleaseUrl = $"https://github.com/{GitHubUsername}/{GitHubRepositoryName}/releases/tag/{TagName}";
                WriteObject(result);
                return;
            }

            var module = MyInvocation.MyCommand?.Module;
            if (module is null)
            {
                result.ErrorMessage = "Module context not available.";
                WriteObject(result);
                return;
            }

            var sb = ScriptBlock.Create(@"
param($u,$r,$t,$tag,$name,$asset,$pre)
Send-GitHubRelease -GitHubUsername $u -GitHubRepositoryName $r -GitHubAccessToken $t -TagName $tag -ReleaseName $name -AssetFilePaths $asset -IsPreRelease:$pre
");
            var bound = module.NewBoundScriptBlock(sb);
            var output = bound.Invoke(GitHubUsername, GitHubRepositoryName, GitHubAccessToken, TagName, ReleaseName, new[] { zip }, IsPreRelease.IsPresent);
            var status = output.Count > 0 ? output[0]?.BaseObject : null;

            if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
            {
                result.Success = gr.Succeeded;
                result.ReleaseUrl = gr.ReleaseUrl;
                result.ErrorMessage = gr.Succeeded ? null : gr.ErrorMessage;
            }
            else if (status is PSObject pso)
            {
                var succeeded = pso.Properties["Succeeded"]?.Value as bool?;
                result.Success = succeeded ?? false;
                result.ReleaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                result.ErrorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
            }
            else
            {
                result.ErrorMessage = "Unexpected result from Send-GitHubRelease.";
            }

            WriteObject(result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
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
