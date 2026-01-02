using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Publishes built modules to repositories or GitHub releases based on publish configuration.
/// </summary>
public sealed class ModulePublisher
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly PowerShellGetClient _powerShellGet;
    private readonly GitHubReleasePublisher _gitHub;

    /// <summary>
    /// Creates a new publisher using the provided logger and the default out-of-process PowerShell runner.
    /// </summary>
    public ModulePublisher(ILogger logger)
        : this(logger, new PowerShellRunner())
    {
    }

    /// <summary>
    /// Creates a new publisher using the provided logger and runner.
    /// </summary>
    public ModulePublisher(ILogger logger, IPowerShellRunner runner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _psResourceGet = new PSResourceGetClient(runner, _logger);
        _powerShellGet = new PowerShellGetClient(runner, _logger);
        _gitHub = new GitHubReleasePublisher(_logger);
    }

    /// <summary>
    /// Publishes based on <paramref name="publish"/> configuration.
    /// </summary>
    public ModulePublishResult Publish(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        IReadOnlyList<ArtefactBuildResult> artefactResults)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (artefactResults is null) throw new ArgumentNullException(nameof(artefactResults));

        if (!publish.Enabled)
        {
            return new ModulePublishResult(
                destination: publish.Destination,
                repositoryName: publish.RepositoryName,
                userName: publish.UserName,
                tagName: null,
                isPreRelease: false,
                assetPaths: Array.Empty<string>(),
                releaseUrl: null,
                succeeded: true,
                errorMessage: null);
        }

        return publish.Destination switch
        {
            PublishDestination.PowerShellGallery => PublishToRepository(publish, plan, buildResult),
            PublishDestination.GitHub => PublishToGitHub(publish, plan, artefactResults),
            _ => throw new NotSupportedException($"Unsupported publish destination: {publish.Destination}")
        };
    }

    private ModulePublishResult PublishToRepository(PublishConfiguration publish, ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var (repositoryName, repoConfig) = ResolveRepository(publish);
        var isPsGallery = string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase);

        var credential = repoConfig?.Credential;
        var hasCredential = credential is not null &&
                            !string.IsNullOrWhiteSpace(credential.UserName) &&
                            !string.IsNullOrWhiteSpace(credential.Secret);

        if (isPsGallery && string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("Publish API key is required for repository publishing to PSGallery.");

        if (!isPsGallery && string.IsNullOrWhiteSpace(publish.ApiKey) && !hasCredential)
            throw new InvalidOperationException("Publish API key or credential is required for repository publishing.");

        var tool = publish.Tool;
        if (tool == PublishTool.Auto)
        {
            try
            {
                return PublishToRepositoryWithTool(PublishTool.PSResourceGet, publish, plan, buildResult, repositoryName, repoConfig);
            }
            catch (PowerShellToolNotAvailableException)
            {
                return PublishToRepositoryWithTool(PublishTool.PowerShellGet, publish, plan, buildResult, repositoryName, repoConfig);
            }
        }

        return PublishToRepositoryWithTool(tool, publish, plan, buildResult, repositoryName, repoConfig);
    }

    private ModulePublishResult PublishToRepositoryWithTool(
        PublishTool tool,
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig)
    {
        var credential = repoConfig?.Credential;
        bool createdRepository = false;

        try
        {
            if (repoConfig is not null && repoConfig.EnsureRegistered && HasRepositoryUris(repoConfig))
            {
                createdRepository = EnsureRepositoryRegistered(tool, repositoryName, repoConfig);
            }

            if (!publish.Force)
                EnsureVersionIsGreaterThanRepository(tool, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease, repositoryName, credential);

            _logger.Info($"Publishing {plan.ModuleName} {FormatSemVer(plan.ResolvedVersion, plan.PreRelease)} to repository '{repositoryName}' using {tool}");

            var modulePath = Path.GetFullPath(buildResult.StagingPath);

            if (tool == PublishTool.PowerShellGet)
            {
                _powerShellGet.Publish(
                    new PowerShellGetPublishOptions(
                        path: modulePath,
                        repository: repositoryName,
                        apiKey: string.IsNullOrWhiteSpace(publish.ApiKey) ? null : publish.ApiKey,
                        credential: credential));
            }
            else
            {
                _psResourceGet.Publish(
                    new PSResourcePublishOptions(
                        path: modulePath,
                        isNupkg: false,
                        repository: repositoryName,
                        apiKey: string.IsNullOrWhiteSpace(publish.ApiKey) ? null : publish.ApiKey,
                        destinationPath: null,
                        skipDependenciesCheck: false,
                        skipModuleManifestValidate: false,
                        credential: credential));
            }
        }
        finally
        {
            if (createdRepository && repoConfig is not null && repoConfig.UnregisterAfterUse)
            {
                try
                {
                    UnregisterRepository(tool, repositoryName);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to unregister repository '{repositoryName}': {ex.Message}");
                }
            }
        }

        return new ModulePublishResult(
            destination: PublishDestination.PowerShellGallery,
            repositoryName: repositoryName,
            userName: null,
            tagName: null,
            isPreRelease: false,
            assetPaths: Array.Empty<string>(),
            releaseUrl: null,
            succeeded: true,
            errorMessage: null);
    }

    private void EnsureVersionIsGreaterThanRepository(
        PublishTool tool,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        string repositoryName,
        RepositoryCredential? credential)
    {
        var publishVersionText = FormatSemVer(moduleVersion, preRelease);
        if (!TryParseSemVer(publishVersionText, out var publishVersion))
            throw new InvalidOperationException($"Could not parse module version for publish: '{publishVersionText}'.");

        SemVer? current = null;
        try
        {
            current = TryGetLatestRepositoryVersion(tool, moduleName, repositoryName, credential);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to query repository version for {moduleName} on '{repositoryName}'. Use -Force to publish without version check. {ex.Message}");
        }

        if (current is null) return;

        if (publishVersion.CompareTo(current.Value) <= 0)
            throw new InvalidOperationException($"Module version '{publishVersionText}' is not greater than repository version '{FormatSemVer(current.Value.Version.ToString(), current.Value.PreRelease)}' for '{moduleName}'. Use -Force to publish anyway.");
    }

    private SemVer? TryGetLatestRepositoryVersion(PublishTool tool, string moduleName, string repositoryName, RepositoryCredential? credential)
    {
        var versions = tool == PublishTool.PowerShellGet
            ? _powerShellGet.Find(
                    new PowerShellGetFindOptions(
                        names: new[] { moduleName },
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Version)
            : _psResourceGet.Find(
                    new PSResourceFindOptions(
                        names: new[] { moduleName },
                        version: null,
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Version);

        SemVer? latest = null;
        foreach (var v in versions)
        {
            if (!TryParseSemVer(v, out var parsed)) continue;
            if (latest is null || parsed.CompareTo(latest.Value) > 0) latest = parsed;
        }
        return latest;
    }

    private static (string RepositoryName, PublishRepositoryConfiguration? Repository) ResolveRepository(PublishConfiguration publish)
    {
        var repoConfig = publish.Repository;

        var name = repoConfig is not null && !string.IsNullOrWhiteSpace(repoConfig.Name)
            ? repoConfig.Name!.Trim()
            : (string.IsNullOrWhiteSpace(publish.RepositoryName) ? "PSGallery" : publish.RepositoryName!.Trim());

        return (name, repoConfig);
    }

    private static bool HasRepositoryUris(PublishRepositoryConfiguration repo)
        => repo is not null &&
           (!string.IsNullOrWhiteSpace(repo.Uri) ||
            !string.IsNullOrWhiteSpace(repo.SourceUri) ||
            !string.IsNullOrWhiteSpace(repo.PublishUri));

    private bool EnsureRepositoryRegistered(PublishTool tool, string repositoryName, PublishRepositoryConfiguration repo)
    {
        if (tool == PublishTool.PowerShellGet)
        {
            var sourceUri = string.IsNullOrWhiteSpace(repo.SourceUri)
                ? (string.IsNullOrWhiteSpace(repo.Uri) ? repo.PublishUri : repo.Uri)
                : repo.SourceUri;
            var publishUri = string.IsNullOrWhiteSpace(repo.PublishUri)
                ? (string.IsNullOrWhiteSpace(repo.Uri) ? repo.SourceUri : repo.Uri)
                : repo.PublishUri;

            if (string.IsNullOrWhiteSpace(sourceUri) || string.IsNullOrWhiteSpace(publishUri))
                throw new InvalidOperationException($"Repository '{repositoryName}' is missing SourceUri/PublishUri/Uri for PowerShellGet registration.");

            return _powerShellGet.EnsureRepositoryRegistered(repositoryName, sourceUri!, publishUri!, trusted: repo.Trusted, timeout: TimeSpan.FromMinutes(2));
        }

        var uri = string.IsNullOrWhiteSpace(repo.Uri)
            ? (string.IsNullOrWhiteSpace(repo.PublishUri) ? repo.SourceUri : repo.PublishUri)
            : repo.Uri;

        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException($"Repository '{repositoryName}' is missing Uri/PublishUri/SourceUri for PSResourceGet registration.");

        return _psResourceGet.EnsureRepositoryRegistered(
            name: repositoryName,
            uri: uri!,
            trusted: repo.Trusted,
            priority: repo.Priority,
            apiVersion: repo.ApiVersion,
            timeout: TimeSpan.FromMinutes(2));
    }

    private void UnregisterRepository(PublishTool tool, string repositoryName)
    {
        if (tool == PublishTool.PowerShellGet)
        {
            _powerShellGet.UnregisterRepository(repositoryName, timeout: TimeSpan.FromMinutes(2));
            return;
        }

        _psResourceGet.UnregisterRepository(repositoryName, timeout: TimeSpan.FromMinutes(2));
    }

    private ModulePublishResult PublishToGitHub(PublishConfiguration publish, ModulePipelinePlan plan, IReadOnlyList<ArtefactBuildResult> artefactResults)
    {
        if (string.IsNullOrWhiteSpace(publish.UserName))
            throw new InvalidOperationException("UserName is required for GitHub publishing.");
        if (string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("API key (token) is required for GitHub publishing.");

        var owner = publish.UserName!.Trim();
        var repo = string.IsNullOrWhiteSpace(publish.RepositoryName) ? plan.ModuleName : publish.RepositoryName!.Trim();

        var defaultTag = "v" + plan.ResolvedVersion;
        var tag = string.IsNullOrWhiteSpace(publish.OverwriteTagName)
            ? defaultTag
            : BuildServices.ReplacePathTokens(publish.OverwriteTagName!, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease);

        var isPreRelease = !string.IsNullOrWhiteSpace(plan.PreRelease) && !publish.DoNotMarkAsPreRelease;

        var selected = SelectPackedArtefacts(artefactResults, publish.ID);
        var assets = selected.Select(a => a.OutputPath).ToArray();

        _logger.Info($"Publishing GitHub release {owner}/{repo} tag '{tag}' with {assets.Length} asset(s)");
        var created = _gitHub.PublishRelease(
            owner: owner,
            repo: repo,
            token: publish.ApiKey,
            tagName: tag,
            releaseName: tag,
            releaseNotes: null,
            commitish: null,
            generateReleaseNotes: publish.GenerateReleaseNotes,
            isDraft: false,
            isPreRelease: isPreRelease,
            assetFilePaths: assets);

        var releaseUrl = string.IsNullOrWhiteSpace(created.HtmlUrl)
            ? $"https://github.com/{owner}/{repo}/releases/tag/{tag}"
            : created.HtmlUrl;

        return new ModulePublishResult(
            destination: PublishDestination.GitHub,
            repositoryName: repo,
            userName: owner,
            tagName: tag,
            isPreRelease: isPreRelease,
            assetPaths: assets,
            releaseUrl: releaseUrl,
            succeeded: true,
            errorMessage: null);
    }

    private static ArtefactBuildResult[] SelectPackedArtefacts(IReadOnlyList<ArtefactBuildResult> artefactResults, string? id)
    {
        var packed = (artefactResults ?? Array.Empty<ArtefactBuildResult>())
            .Where(a => a is not null && (a.Type == ArtefactType.Packed || a.Type == ArtefactType.ScriptPacked))
            .ToArray();

        if (packed.Length == 0)
            throw new InvalidOperationException("No packed artefacts were produced; cannot publish.");

        if (string.IsNullOrWhiteSpace(id))
            return new[] { packed[0] };

        var idValue = id!.Trim();
        var selected = packed.Where(a => string.Equals(a.Id, idValue, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
        {
            var available = packed.Select(a => a.Id).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var availText = available.Length == 0 ? "(none)" : string.Join(", ", available);
            throw new InvalidOperationException($"No packed artefacts matched ID '{id}'. Available IDs: {availText}");
        }
        return selected;
    }

    private readonly struct SemVer : IComparable<SemVer>
    {
        public Version Version { get; }
        public string? PreRelease { get; }

        public SemVer(Version version, string? preRelease)
        {
            Version = version;
            PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
        }

        public int CompareTo(SemVer other)
        {
            var c = Version.CompareTo(other.Version);
            if (c != 0) return c;

            var a = PreRelease;
            var b = other.PreRelease;
            if (a is null && b is null) return 0;
            if (a is null) return 1; // stable > prerelease
            if (b is null) return -1;

            return ComparePreRelease(a, b);
        }

        private static int ComparePreRelease(string a, string b)
        {
            var aa = a.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var bb = b.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var len = Math.Max(aa.Length, bb.Length);
            for (var i = 0; i < len; i++)
            {
                if (i >= aa.Length) return -1;
                if (i >= bb.Length) return 1;

                var pa = aa[i];
                var pb = bb[i];

                var na = int.TryParse(pa, out var ia);
                var nb = int.TryParse(pb, out var ib);
                if (na && nb)
                {
                    var c = ia.CompareTo(ib);
                    if (c != 0) return c;
                }
                else
                {
                    var c = StringComparer.OrdinalIgnoreCase.Compare(pa, pb);
                    if (c != 0) return c;
                }
            }
            return 0;
        }
    }

    private static bool TryParseSemVer(string text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();
        var dash = t.IndexOf('-');
        var main = dash >= 0 ? t.Substring(0, dash) : t;
        var pre = dash >= 0 ? t.Substring(dash + 1) : null;
        if (!Version.TryParse(main, out var v)) return false;
        version = new SemVer(v, pre);
        return true;
    }

    private static string FormatSemVer(string moduleVersion, string? preRelease)
        => string.IsNullOrWhiteSpace(preRelease) ? moduleVersion : moduleVersion + "-" + preRelease;
}
