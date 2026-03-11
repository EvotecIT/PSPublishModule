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
[Cmdlet(VerbsData.Publish, "NugetPackage", SupportsShouldProcess = true)]
public sealed class PublishNugetPackageCommand : PSCmdlet
{
    /// <summary>Directory to search for NuGet packages.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] Path { get; set; } = Array.Empty<string>();

    /// <summary>API key used to authenticate against the NuGet feed.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>NuGet feed URL.</summary>
    [Parameter]
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

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
            var logger = new CmdletLogger(this, MyInvocation?.BoundParameters.ContainsKey("Verbose") == true);
            var service = new NuGetPackagePublishService(logger);
            var resolvedRoots = ResolveRoots(Path);
            var serviceResult = service.Execute(new NuGetPackagePublishRequest
            {
                Roots = resolvedRoots,
                ApiKey = ApiKey,
                Source = Source,
                SkipDuplicate = SkipDuplicate.IsPresent
            }, package => ShouldProcess(package, $"Publish NuGet package to {Source}"));

            WriteObject(ToCmdletResult(serviceResult));
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

    private static PublishNugetPackageResult ToCmdletResult(PowerForge.NuGetPackagePublishResult result)
    {
        var mapped = new PublishNugetPackageResult
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage
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
    }
}
