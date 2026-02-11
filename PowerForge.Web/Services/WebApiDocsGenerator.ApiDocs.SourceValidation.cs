using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static void ValidateSourceUrlPatternConsistency(
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        List<string> warnings)
    {
        if (options is null || types is null || warnings is null)
            return;
        if (string.IsNullOrWhiteSpace(options.SourceUrlPattern))
            return;
        if (options.SourceUrlMappings.Count > 0)
            return; // Explicit mapping rules own source URL selection.
        if (options.SourceUrlPattern.IndexOf("{root}", StringComparison.OrdinalIgnoreCase) >= 0)
            return; // Dynamic repo token already adapts per discovered source root.
        if (!TryExtractGitHubRepoName(options.SourceUrlPattern, out var repoName))
            return;

        var duplicatedRoots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void ObserveSource(ApiSourceLink? source)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Path))
                return;

            var segments = source.Path
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2)
                return;
            // Heuristic: sibling repo builds often produce paths like Repo/Repo/... when sourceRoot points
            // to the parent folder. If sourceUrl points at a different repo, links usually 404.
            if (!string.Equals(segments[0], segments[1], StringComparison.OrdinalIgnoreCase))
                return;

            if (!duplicatedRoots.TryAdd(segments[0], 1))
                duplicatedRoots[segments[0]]++;
        }

        foreach (var type in types)
        {
            ObserveSource(type.Source);
            ObserveMemberSources(type.Methods, ObserveSource);
            ObserveMemberSources(type.Constructors, ObserveSource);
            ObserveMemberSources(type.Properties, ObserveSource);
            ObserveMemberSources(type.Fields, ObserveSource);
            ObserveMemberSources(type.Events, ObserveSource);
            ObserveMemberSources(type.ExtensionMethods, ObserveSource);
        }

        if (duplicatedRoots.Count == 0)
            return;

        var dominant = duplicatedRoots
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        var dominantRoot = dominant.Key;
        if (dominant.Value < 5)
            return;
        if (string.Equals(dominantRoot, repoName, StringComparison.OrdinalIgnoreCase))
            return;

        warnings.Add(
            $"SourceUrlPattern repo '{repoName}' may not match discovered source root '{dominantRoot}' (paths look like '{dominantRoot}/{dominantRoot}/...'). " +
            "This often produces broken source/edit links; adjust sourceUrl (or sourceRoot) for this API step.");
    }

    private static void ObserveMemberSources(IEnumerable<ApiMemberModel> members, Action<ApiSourceLink?> observeSource)
    {
        if (members is null || observeSource is null)
            return;
        foreach (var member in members)
            observeSource(member.Source);
    }

    private static bool TryExtractGitHubRepoName(string pattern, out string repoName)
    {
        repoName = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var candidate = pattern
            .Replace("{path}", "sample/path.cs", StringComparison.OrdinalIgnoreCase)
            .Replace("{pathNoRoot}", "sample/path.cs", StringComparison.OrdinalIgnoreCase)
            .Replace("{pathNoPrefix}", "sample/path.cs", StringComparison.OrdinalIgnoreCase)
            .Replace("{root}", "SampleRepo", StringComparison.OrdinalIgnoreCase)
            .Replace("{line}", "1", StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return false;

        repoName = segments[1];
        return !string.IsNullOrWhiteSpace(repoName);
    }
}
