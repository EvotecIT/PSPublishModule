using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateBuildConfiguration(
        string repositoryRoot,
        string projectDirectory,
        PbxObject item,
        IReadOnlyDictionary<string, PbxObject> objects,
        IReadOnlyDictionary<string, string> parents,
        IDictionary<string, string?> cache,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var buildSettings = ReadPbxDictionary(item.Body, "buildSettings");
        var buildSettingAssignments = buildSettings is null
            ? Array.Empty<KeyValuePair<string, string>>()
            : ReadPbxAssignments(buildSettings).ToArray();
        var baseConfigurationReference = ReadPbxScalar(item.Body, "baseConfigurationReference")?
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(baseConfigurationReference))
        {
            var baseConfigurationPath = ResolvePbxObjectPath(
                projectDirectory,
                baseConfigurationReference!,
                objects,
                parents,
                cache,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (baseConfigurationPath is null)
                throw new InvalidOperationException($"Xcode base configuration uses an external source tree: {metadataPath}");
            EnsureNoGeneratedOutputOverlap(baseConfigurationPath, generatedOutputPaths, "Xcode base configuration");
            EnsureTrackedFile(repositoryRoot, baseConfigurationPath, "Xcode base configuration");
            var basePreprocess = ResolveInfoPlistPreprocessFromXcconfigGraph(
                repositoryRoot,
                baseConfigurationPath,
                new HashSet<string>(GetPathComparer()));
            var projectPreprocess = ResolveInfoPlistPreprocessSetting(buildSettingAssignments);
            var effectivePreprocess = projectPreprocess ?? basePreprocess ?? false;
            EnsureTrackedXcconfigGraph(
                repositoryRoot,
                projectDirectory,
                baseConfigurationPath,
                generatedOutputPaths,
                new HashSet<string>(GetPathComparer()),
                effectivePreprocess);
            if (buildSettings is not null)
            {
                ValidateBuildSettingAssignments(
                    repositoryRoot,
                    projectDirectory,
                    buildSettingAssignments,
                    generatedOutputPaths,
                    "PBX build settings",
                    effectivePreprocess);
            }
            return;
        }

        if (buildSettings is null)
            return;
        ValidateBuildSettingAssignments(
            repositoryRoot,
            projectDirectory,
            buildSettingAssignments,
            generatedOutputPaths,
            "PBX build settings");
    }

    private void EnsureTrackedXcconfigGraph(
        string repositoryRoot,
        string projectDirectory,
        string configPath,
        IReadOnlyCollection<string> generatedOutputPaths,
        ISet<string> visited,
        bool? effectiveInfoPlistPreprocess = null)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!visited.Add(fullPath))
            return;
        var contents = File.ReadAllText(fullPath);
        ValidateBuildSettingAssignments(
            repositoryRoot,
            projectDirectory,
            ReadXcconfigAssignments(contents),
            generatedOutputPaths,
            $"xcconfig '{fullPath}'",
            effectiveInfoPlistPreprocess);
        foreach (Match include in Regex.Matches(
                     contents,
                     "(?m)^[ \\t]*#include(?<optional>\\?)?[ \\t]+[\\\"<](?<path>[^\\\">]+)[\\\">]",
                     RegexOptions.CultureInvariant))
        {
            var value = include.Groups["path"].Value.Trim();
            var includedPath = ResolvePbxPath(
                Path.GetDirectoryName(fullPath)!,
                value,
                "xcconfig include");
            EnsurePathWithinRepository(repositoryRoot, includedPath, "Xcode xcconfig include");
            EnsureNoGeneratedOutputOverlap(includedPath, generatedOutputPaths, "Xcode xcconfig include");
            if (!File.Exists(includedPath))
            {
                if (include.Groups["optional"].Success)
                    continue;
                throw new FileNotFoundException(
                    $"Xcode xcconfig include cannot be proven at the exact source commit: {includedPath}",
                    includedPath);
            }
            EnsureTrackedFile(repositoryRoot, includedPath, "Xcode xcconfig include");
            EnsureTrackedXcconfigGraph(
                repositoryRoot,
                projectDirectory,
                includedPath,
                generatedOutputPaths,
                visited,
                effectiveInfoPlistPreprocess);
        }
    }

    private static bool? ResolveInfoPlistPreprocessFromXcconfigGraph(
        string repositoryRoot,
        string configPath,
        ISet<string> visited,
        bool? inherited = null)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!visited.Add(fullPath))
            throw new InvalidOperationException($"Xcode xcconfig include graph contains a cycle at '{fullPath}'.");
        try
        {
            var contents = Regex.Replace(File.ReadAllText(fullPath), "\\\\[ \\t]*\\r?\\n", " ");
            var enabled = inherited;
            var conditionedEnabled = false;
            foreach (var line in contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var include = Regex.Match(
                    line,
                    "^[ \\t]*#include(?<optional>\\?)?[ \\t]+[\\\"<](?<path>[^\\\">]+)[\\\">]",
                    RegexOptions.CultureInvariant);
                if (include.Success)
                {
                    var includedPath = ResolvePbxPath(
                        Path.GetDirectoryName(fullPath)!,
                        include.Groups["path"].Value.Trim(),
                        "xcconfig include");
                    EnsurePathWithinRepository(repositoryRoot, includedPath, "Xcode xcconfig include");
                    if (File.Exists(includedPath))
                    {
                        var included = ResolveInfoPlistPreprocessFromXcconfigGraph(
                            repositoryRoot,
                            includedPath,
                            visited,
                            enabled);
                        if (included is not null)
                            enabled = included;
                    }
                    continue;
                }

                var assignment = Regex.Match(
                    line,
                    "^[ \\t]*(?<key>INFOPLIST_PREPROCESS(?:\\[[^\\]]+\\])*)[ \\t]*(?<op>\\?=|\\+=|=)[ \\t]*(?<value>.*?)[ \\t]*(?://.*)?$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!assignment.Success)
                    continue;
                var key = assignment.Groups["key"].Value;
                var value = assignment.Groups["value"].Value.Trim();
                if (!value.Equals("YES", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("NO", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Xcode build setting {key} must be YES or NO for an exact-source Apple build; received '{value}'.");
                }
                if (assignment.Groups["op"].Value.Equals("+=", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Xcode build setting {key} cannot use '+=' for a boolean exact-source setting.");
                if (key.IndexOf('[') >= 0)
                {
                    conditionedEnabled |= value.Equals("YES", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (assignment.Groups["op"].Value.Equals("?=", StringComparison.Ordinal) && enabled is not null)
                    continue;
                enabled = value.Equals("YES", StringComparison.OrdinalIgnoreCase);
            }
            return conditionedEnabled || enabled == true
                ? true
                : enabled;
        }
        finally
        {
            visited.Remove(fullPath);
        }
    }
}
