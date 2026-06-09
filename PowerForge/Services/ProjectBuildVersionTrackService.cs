using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Expands declarative version tracks into the project-version map used by the repository release engine.
/// </summary>
internal sealed class ProjectBuildVersionTrackService
{
    private readonly ILogger _logger;
    private readonly NuGetPackageVersionResolver _resolver;

    public ProjectBuildVersionTrackService(ILogger logger, NuGetPackageVersionResolver? resolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = resolver ?? new NuGetPackageVersionResolver(_logger);
    }

    public Dictionary<string, string>? ResolveExpectedVersionMap(
        ProjectBuildConfiguration config,
        RepositoryCredential? credential)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ExpandTracks(config.VersionTracks, config.ExpectedVersion, config.NugetSource, credential, config.IncludePrerelease))
            resolved[entry.Key] = entry.Value;

        var explicitMap = config.ExpectedVersionMap;
        if (explicitMap is not null)
        {
            foreach (var entry in explicitMap)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                resolved[entry.Key.Trim()] = entry.Value.Trim();
            }
        }

        return resolved.Count == 0 ? null : resolved;
    }

    private IEnumerable<KeyValuePair<string, string>> ExpandTracks(
        IReadOnlyDictionary<string, ProjectBuildVersionTrack>? tracks,
        string? defaultExpectedVersion,
        IReadOnlyList<string>? defaultSources,
        RepositoryCredential? credential,
        bool defaultIncludePrerelease)
    {
        if (tracks is null || tracks.Count == 0)
            yield break;

        foreach (var entry in tracks)
        {
            var trackName = string.IsNullOrWhiteSpace(entry.Key) ? "<unnamed>" : entry.Key.Trim();
            var track = entry.Value ?? throw new ArgumentException($"VersionTracks entry '{trackName}' is null.", nameof(tracks));
            var expectedVersion = NormalizeNullable(track.ExpectedVersion) ?? NormalizeNullable(defaultExpectedVersion);
            if (string.IsNullOrWhiteSpace(expectedVersion))
                throw new ArgumentException($"VersionTracks['{trackName}'] must define ExpectedVersion or rely on a non-empty top-level ExpectedVersion.", nameof(tracks));

            var resolvedVersion = ResolveTrackVersion(trackName, track, expectedVersion!, defaultSources, credential, defaultIncludePrerelease);
            foreach (var project in ResolveProjects(trackName, track))
                yield return new KeyValuePair<string, string>(project, resolvedVersion);
        }
    }

    private string ResolveTrackVersion(
        string trackName,
        ProjectBuildVersionTrack track,
        string expectedVersion,
        IReadOnlyList<string>? defaultSources,
        RepositoryCredential? credential,
        bool defaultIncludePrerelease)
    {
        if (Version.TryParse(expectedVersion, out var exact))
            return exact.ToString();

        var anchorPackageId = NormalizeNullable(track.AnchorPackageId) ?? NormalizeNullable(track.AnchorProject);
        if (anchorPackageId is null)
            throw new ArgumentException($"VersionTracks['{trackName}'] uses pattern version '{expectedVersion}' but does not define AnchorProject or AnchorPackageId.");

        var sources = (track.NugetSource ?? defaultSources ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var includePrerelease = track.IncludePrerelease ?? defaultIncludePrerelease;
        var current = _resolver.ResolveLatest(anchorPackageId, sources, credential, includePrerelease);
        if (current is null)
            _logger.Info($"Version track '{trackName}' could not resolve an existing version for anchor '{anchorPackageId}'. Using the X-pattern baseline.");

        return VersionPatternStepper.Step(expectedVersion, current);
    }

    private static IReadOnlyList<string> ResolveProjects(string trackName, ProjectBuildVersionTrack track)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anchorProject = NormalizeNullable(track.AnchorProject);
        var hasExplicitAnchorPackageId = NormalizeNullable(track.AnchorPackageId) is not null;

        if (track.Projects is not null)
        {
            foreach (var project in track.Projects)
            {
                var normalized = NormalizeNullable(project);
                if (normalized is not null)
                    projects.Add(normalized);
            }
        }

        if (anchorProject is not null)
            projects.Add(anchorProject);

        if (hasExplicitAnchorPackageId && anchorProject is null && projects.Count > 0)
        {
            throw new ArgumentException(
                $"VersionTracks['{trackName}'] defines AnchorPackageId but not AnchorProject. " +
                "Specify AnchorProject so the anchor project can be stamped automatically, or move the anchor project into Projects explicitly.");
        }

        if (projects.Count == 0)
            throw new ArgumentException($"VersionTracks['{trackName}'] must define at least one project in Projects or AnchorProject.");

        return projects.ToArray();
    }

    private static string? NormalizeNullable(string? value)
    {
        var candidate = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var trimmed = candidate.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
