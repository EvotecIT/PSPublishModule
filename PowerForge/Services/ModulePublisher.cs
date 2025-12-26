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
        var repository = string.IsNullOrWhiteSpace(publish.RepositoryName) ? "PSGallery" : publish.RepositoryName!.Trim();

        if (string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("Publish API key is required for repository publishing.");

        if (!publish.Force)
            EnsureVersionIsGreaterThanRepository(plan.ModuleName, plan.ResolvedVersion, plan.PreRelease, repository);

        _logger.Info($"Publishing {plan.ModuleName} {FormatSemVer(plan.ResolvedVersion, plan.PreRelease)} to repository '{repository}'");

        _psResourceGet.Publish(new PSResourcePublishOptions(
            path: Path.GetFullPath(buildResult.StagingPath),
            isNupkg: false,
            repository: repository,
            apiKey: publish.ApiKey,
            destinationPath: null,
            skipDependenciesCheck: false,
            skipModuleManifestValidate: false));

        return new ModulePublishResult(
            destination: PublishDestination.PowerShellGallery,
            repositoryName: repository,
            userName: null,
            tagName: null,
            isPreRelease: false,
            assetPaths: Array.Empty<string>(),
            releaseUrl: null,
            succeeded: true,
            errorMessage: null);
    }

    private void EnsureVersionIsGreaterThanRepository(string moduleName, string moduleVersion, string? preRelease, string repository)
    {
        var publishVersionText = FormatSemVer(moduleVersion, preRelease);
        if (!TryParseSemVer(publishVersionText, out var publishVersion))
            throw new InvalidOperationException($"Could not parse module version for publish: '{publishVersionText}'.");

        IReadOnlyList<PSResourceInfo> found;
        try
        {
            found = _psResourceGet.Find(
                new PSResourceFindOptions(
                    names: new[] { moduleName },
                    version: null,
                    prerelease: true,
                    repositories: new[] { repository }),
                timeout: TimeSpan.FromMinutes(2));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to query repository version for {moduleName} on '{repository}'. Use -Force to publish without version check. {ex.Message}");
        }

        var hit = found.FirstOrDefault(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (hit is null || string.IsNullOrWhiteSpace(hit.Version)) return;
        if (!TryParseSemVer(hit.Version, out var current))
            throw new InvalidOperationException($"Could not parse repository version '{hit.Version}' for module '{moduleName}'. Use -Force to publish without version check.");

        if (publishVersion.CompareTo(current) <= 0)
            throw new InvalidOperationException($"Module version '{publishVersionText}' is not greater than repository version '{hit.Version}' for '{moduleName}'. Use -Force to publish anyway.");
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
