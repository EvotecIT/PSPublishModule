using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class GitHubReleaseAssetWorkflowService
{
    private readonly Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> _publishRelease;

    public GitHubReleaseAssetWorkflowService(
        ILogger logger,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult>? publishRelease = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        _publishRelease = publishRelease ?? (request => new GitHubReleasePublisher(logger).PublishRelease(request));
    }

    public IReadOnlyList<GitHubReleaseAssetWorkflowResult> Execute(GitHubReleaseAssetWorkflowRequest request, bool publish)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var results = new List<GitHubReleaseAssetWorkflowResult>();
        var projectPaths = request.ProjectPaths ?? Array.Empty<string>();
        if (projectPaths.Count == 0)
            return results;

        if (projectPaths.Count > 1 && !string.IsNullOrWhiteSpace(request.ZipPath))
        {
            results.Add(NewFailure("ZipPath override is not supported when multiple ProjectPath values are provided."));
            return results;
        }

        var entries = new List<GitHubReleaseAssetEntry>();
        foreach (var projectPath in projectPaths)
        {
            var prepared = TryPrepareEntry(projectPath, request);
            if (!prepared.Success)
            {
                results.Add(NewFailure(prepared.ErrorMessage!, zipPath: prepared.ZipPath));
                continue;
            }

            entries.Add(prepared.Entry!);
        }

        foreach (var group in entries.GroupBy(entry => entry.TagName, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var assets = group.Select(entry => entry.ZipPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var succeeded = true;
            var releaseUrl = $"https://github.com/{request.Owner}/{request.Repository}/releases/tag/{first.TagName}";
            string? errorMessage = null;

            if (publish)
            {
                try
                {
                    var publishResult = _publishRelease(new GitHubReleasePublishRequest
                    {
                        Owner = request.Owner,
                        Repository = request.Repository,
                        Token = request.Token,
                        TagName = first.TagName,
                        ReleaseName = first.ReleaseName,
                        GenerateReleaseNotes = request.GenerateReleaseNotes,
                        IsPreRelease = request.IsPreRelease,
                        ReuseExistingReleaseOnConflict = true,
                        AssetFilePaths = assets
                    });

                    succeeded = publishResult.Succeeded;
                    releaseUrl = publishResult.HtmlUrl;
                }
                catch (Exception ex)
                {
                    succeeded = false;
                    releaseUrl = null;
                    errorMessage = ex.Message;
                }
            }

            foreach (var entry in group)
            {
                results.Add(new GitHubReleaseAssetWorkflowResult
                {
                    Success = succeeded,
                    TagName = entry.TagName,
                    ReleaseName = entry.ReleaseName,
                    ZipPath = entry.ZipPath,
                    ReleaseUrl = releaseUrl,
                    ErrorMessage = succeeded ? null : errorMessage
                });
            }
        }

        return results;
    }

    private static PreparedEntry TryPrepareEntry(string? projectPath, GitHubReleaseAssetWorkflowRequest request)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return PreparedEntry.Failure("ProjectPath contains an empty value.");

        var normalizedProjectPath = projectPath!;
        if (!Directory.Exists(normalizedProjectPath) && !File.Exists(normalizedProjectPath))
            return PreparedEntry.Failure($"Project path '{normalizedProjectPath}' not found.");

        var csproj = ResolveCsproj(normalizedProjectPath);
        if (csproj is null)
            return PreparedEntry.Failure($"No csproj found in {normalizedProjectPath}");

        var projectName = Path.GetFileNameWithoutExtension(csproj) ?? "Project";
        var csprojDir = Path.GetDirectoryName(csproj) ?? normalizedProjectPath;

        var version = request.Version;
        if (string.IsNullOrWhiteSpace(version) && !CsprojVersionEditor.TryGetVersion(csproj, out version!))
            return PreparedEntry.Failure($"Version not found in '{csproj}'");
        var resolvedVersion = version!;

        var zipPath = string.IsNullOrWhiteSpace(request.ZipPath)
            ? Path.Combine(csprojDir, "bin", "Release", $"{projectName}.{resolvedVersion}.zip")
            : request.ZipPath!;

        if (!File.Exists(zipPath))
            return PreparedEntry.Failure($"Zip file '{zipPath}' not found.", zipPath);

        string resolvedTagName;
        if (!string.IsNullOrWhiteSpace(request.TagName))
        {
            resolvedTagName = request.TagName!;
        }
        else
        {
            var tagTemplate = request.TagTemplate;
            if (tagTemplate is not null && tagTemplate.Trim().Length > 0)
            {
                resolvedTagName = tagTemplate.Replace("{Project}", projectName).Replace("{Version}", resolvedVersion);
            }
            else
            {
                var includeProjectNameInTag = request.IncludeProjectNameInTag;
                resolvedTagName = includeProjectNameInTag
                    ? $"{projectName}-v{resolvedVersion}"
                    : $"v{resolvedVersion}";
            }
        }

        var releaseName = string.IsNullOrWhiteSpace(request.ReleaseName) ? resolvedTagName : request.ReleaseName!;

        return PreparedEntry.FromEntry(new GitHubReleaseAssetEntry
        {
            TagName = resolvedTagName,
            ReleaseName = releaseName,
            ZipPath = zipPath
        });
    }

    private static string? ResolveCsproj(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return null;

        return Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static GitHubReleaseAssetWorkflowResult NewFailure(string errorMessage, string? zipPath = null)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            ZipPath = zipPath
        };

    private sealed class GitHubReleaseAssetEntry
    {
        public string TagName { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
        public string ZipPath { get; set; } = string.Empty;
    }

    private sealed class PreparedEntry
    {
        public bool Success { get; private set; }
        public GitHubReleaseAssetEntry? Entry { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? ZipPath { get; private set; }

        public static PreparedEntry FromEntry(GitHubReleaseAssetEntry entry)
            => new() { Success = true, Entry = entry };

        public static PreparedEntry Failure(string errorMessage, string? zipPath = null)
            => new() { Success = false, ErrorMessage = errorMessage, ZipPath = zipPath };
    }
}
