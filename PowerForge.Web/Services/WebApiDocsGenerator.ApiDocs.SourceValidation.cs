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

        var observedPaths = CollectObservedSourcePaths(types);
        if (observedPaths.Count == 0)
            return;

        if (options.SourceUrlMappings.Count > 0)
            ValidateSourceUrlMappings(options, observedPaths, warnings);

        ValidateSourceUrlPatternRepoConsistency(options, observedPaths, warnings);
        ValidateSourceUrlPatternRootCoverage(options, observedPaths, warnings);
        ValidateSourceUrlDuplicatePathHints(types, warnings);
    }

    private static void ObserveMemberSources(IEnumerable<ApiMemberModel> members, Action<ApiSourceLink?> observeSource)
    {
        if (members is null || observeSource is null)
            return;
        foreach (var member in members)
            observeSource(member.Source);
    }

    private static List<string> CollectObservedSourcePaths(IReadOnlyList<ApiTypeModel> types)
    {
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ObserveSource(ApiSourceLink? source)
        {
            var normalized = NormalizeSourcePathForValidation(source?.Path);
            if (!string.IsNullOrWhiteSpace(normalized))
                observed.Add(normalized);
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

        return observed
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateSourceUrlMappings(
        WebApiDocsOptions options,
        IReadOnlyList<string> observedPaths,
        List<string> warnings)
    {
        if (options?.SourceUrlMappings is null || options.SourceUrlMappings.Count == 0 || observedPaths.Count == 0)
            return;

        var seenPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in options.SourceUrlMappings)
        {
            if (mapping is null)
                continue;

            var normalizedPrefix = NormalizeSourcePathForValidation(mapping.PathPrefix);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
                continue;

            if (!seenPrefixes.Add(normalizedPrefix))
            {
                warnings.Add($"API docs source: duplicate sourceUrlMappings pathPrefix '{normalizedPrefix}'. Keep only one mapping per prefix to avoid ambiguous URL generation.");
                continue;
            }

            var hasAnyMatch = observedPaths.Any(path => PathMatchesPrefixForValidation(path, normalizedPrefix));
            if (!hasAnyMatch)
            {
                warnings.Add($"API docs source: sourceUrlMappings entry for '{normalizedPrefix}' did not match any discovered source paths. Check pathPrefix/sourceRoot alignment for this API step.");
            }
        }
    }

    private static void ValidateSourceUrlPatternRepoConsistency(
        WebApiDocsOptions options,
        IReadOnlyList<string> observedPaths,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(options.SourceUrlPattern))
            return;
        if (options.SourceUrlMappings.Count > 0)
            return; // Explicit mapping rules own source URL selection.
        if (options.SourceUrlPattern.IndexOf("{root}", StringComparison.OrdinalIgnoreCase) >= 0)
            return; // Dynamic repo token already adapts per discovered source root.
        if (!TryExtractGitHubRepoName(options.SourceUrlPattern, out var repoName))
            return;

        var duplicatedRoots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in observedPaths)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2)
                continue;
            // Heuristic: sibling repo builds often produce paths like Repo/Repo/... when sourceRoot points
            // to the parent folder. If sourceUrl points at a different repo, links usually 404.
            if (!string.Equals(segments[0], segments[1], StringComparison.OrdinalIgnoreCase))
                continue;

            if (!duplicatedRoots.TryAdd(segments[0], 1))
                duplicatedRoots[segments[0]]++;
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

    private static void ValidateSourceUrlPatternRootCoverage(
        WebApiDocsOptions options,
        IReadOnlyList<string> observedPaths,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(options.SourceUrlPattern))
            return;
        if (options.SourceUrlMappings.Count > 0)
            return;
        if (options.SourceUrlPattern.IndexOf("{root}", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        var roots = observedPaths
            .Select(GetFirstSourceSegmentForValidation)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        if (roots.Length == 0)
            return;

        var byRoot = roots
            .GroupBy(static r => r, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.OrdinalIgnoreCase);
        if (byRoot.Count <= 1)
            return;

        // Require at least two "material" roots so we don't warn on tiny outliers.
        var material = byRoot
            .Where(static kvp => kvp.Value >= 3)
            .OrderByDescending(static kvp => kvp.Value)
            .ThenBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (material.Length <= 1)
            return;

        var preview = string.Join(", ", material.Take(4).Select(static kvp => $"{kvp.Key}({kvp.Value})"));
        var more = material.Length > 4 ? $" (+{material.Length - 4} more)" : string.Empty;
        warnings.Add(
            $"API docs source: sourceUrlPattern does not use '{{root}}' but discovered source paths span multiple roots: {preview}{more}. " +
            "Use sourceUrlMappings (preferred) or include {root} in sourceUrlPattern for mixed-repo API docs.");
    }

    private static void ValidateSourceUrlDuplicatePathHints(
        IReadOnlyList<ApiTypeModel> types,
        List<string> warnings)
    {
        if (types is null || warnings is null)
            return;

        var hintCount = 0;
        var samples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ObserveSource(ApiSourceLink? source)
        {
            if (source is null ||
                string.IsNullOrWhiteSpace(source.Path) ||
                string.IsNullOrWhiteSpace(source.Url))
                return;

            var sourcePath = NormalizeSourcePathForValidation(source.Path);
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;
            if (!TryExtractGitHubFilePath(source.Url, out var githubFilePath))
                return;
            if (string.IsNullOrWhiteSpace(githubFilePath))
                return;

            if (!TryBuildPathDuplicationHint(sourcePath, githubFilePath, out var hint))
                return;

            hintCount++;
            if (samples.Count < 8 && !string.IsNullOrWhiteSpace(hint))
                samples.Add(hint);
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

        if (hintCount < 3)
            return;

        var samplePreview = string.Join(", ", samples.Take(4));
        var more = samples.Count > 4 ? $" (+{samples.Count - 4} more samples)" : string.Empty;
        warnings.Add(
            $"API docs source: detected likely duplicated path prefixes in GitHub source URLs for {hintCount} symbol(s) (samples: {samplePreview}{more}). " +
            "Check sourceUrl/sourceUrlMappings/sourcePathPrefix; in mixed layouts prefer pathNoPrefix with stripPathPrefix:true.");
    }

    private static bool TryBuildPathDuplicationHint(string sourcePath, string githubFilePath, out string hint)
    {
        hint = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(githubFilePath))
            return false;
        if (string.Equals(sourcePath, githubFilePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var sourceFirst = GetFirstSourceSegmentForValidation(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceFirst))
            return false;
        if (!githubFilePath.EndsWith("/" + sourcePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var prefixLength = githubFilePath.Length - sourcePath.Length - 1;
        if (prefixLength <= 0)
            return false;

        var extraPrefix = githubFilePath.Substring(0, prefixLength).Trim('/');
        if (string.IsNullOrWhiteSpace(extraPrefix))
            return false;

        var extraTail = extraPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (!string.Equals(extraTail, sourceFirst, StringComparison.OrdinalIgnoreCase))
            return false;

        hint = $"{extraTail}/{sourcePath}";
        return true;
    }

    private static bool TryExtractGitHubFilePath(string url, out string filePath)
    {
        filePath = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var absolute = uri.AbsolutePath;
        var markerIndex = absolute.IndexOf("/blob/", StringComparison.OrdinalIgnoreCase);
        var markerLength = "/blob/".Length;
        if (markerIndex < 0)
        {
            markerIndex = absolute.IndexOf("/edit/", StringComparison.OrdinalIgnoreCase);
            markerLength = "/edit/".Length;
        }

        if (markerIndex < 0)
            return false;

        var afterMarker = absolute.Substring(markerIndex + markerLength);
        var firstSlash = afterMarker.IndexOf('/', StringComparison.Ordinal);
        if (firstSlash < 0 || firstSlash + 1 >= afterMarker.Length)
            return false;

        filePath = NormalizeSourcePathForValidation(afterMarker.Substring(firstSlash + 1));
        return !string.IsNullOrWhiteSpace(filePath);
    }

    private static string NormalizeSourcePathForValidation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return TrimLeadingRelativeSegments(value.Replace('\\', '/').Trim().Trim('/'));
    }

    private static bool PathMatchesPrefixForValidation(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
            return false;
        if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
            return true;
        return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFirstSourceSegmentForValidation(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var normalized = NormalizeSourcePathForValidation(path);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;
        var slash = normalized.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? normalized : normalized.Substring(0, slash);
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
