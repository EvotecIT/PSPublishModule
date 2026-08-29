using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Frameworks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static PlannedEvaluation EvaluatePlannedProject(
        string projectPath,
        string? configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath = null)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry environmentProperty in Environment.GetEnvironmentVariables())
        {
            if (environmentProperty.Key is string name &&
                environmentProperty.Value is not null &&
                Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant))
            {
                properties[name] = environmentProperty.Value.ToString() ?? string.Empty;
            }
        }
        foreach (var globalProperty in new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration ?? string.Empty,
            ["TargetFramework"] = targetFramework ?? string.Empty,
            ["TargetFrameworkIdentifier"] = string.Empty,
            ["TargetFrameworkVersion"] = string.Empty,
            ["TargetFrameworkProfile"] = string.Empty,
            ["TargetPlatformIdentifier"] = string.Empty,
            ["TargetPlatformVersion"] = string.Empty,
            ["MSBuildProjectDirectory"] = projectDirectory,
            ["MSBuildProjectFullPath"] = fullProjectPath,
            ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(fullProjectPath)
        })
        {
            properties[globalProperty.Key] = globalProperty.Value;
        }
        SetDerivedTargetFrameworkProperties(properties, targetFramework);

        var items = new List<PlannedItem>();
        var definitions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(projectDirectory);
        var packagesProps = FindNearestBuildFile(directory, "Directory.Packages.props");
        var buildProps = FindNearestBuildFile(directory, "Directory.Build.props");
        var buildTargets = FindNearestBuildFile(directory, "Directory.Build.targets");
        if (packagesProps is not null)
            EvaluatePlannedFile(packagesProps, fullProjectPath, properties, items, definitions, visited, plannedProjectContentsByPath);
        if (buildProps is not null)
            EvaluatePlannedFile(buildProps, fullProjectPath, properties, items, definitions, visited, plannedProjectContentsByPath);
        EvaluatePlannedFile(fullProjectPath, fullProjectPath, properties, items, definitions, visited, plannedProjectContentsByPath);
        if (buildTargets is not null)
            EvaluatePlannedFile(buildTargets, fullProjectPath, properties, items, definitions, visited, plannedProjectContentsByPath);
        return new PlannedEvaluation(properties, items);
    }

    private static string ResolvePlannedProjectVersion(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var evaluation = EvaluatePlannedProject(project.CsprojPath, configuration, targetFramework: null);
        foreach (var propertyName in new[] { "PackageVersion", "Version" })
        {
            if (evaluation.Properties.TryGetValue(propertyName, out var value) && !string.IsNullOrWhiteSpace(value))
                return ExpandPlannedProperties(value, evaluation.Properties);
        }

        if (evaluation.Properties.TryGetValue("VersionPrefix", out var prefix) && !string.IsNullOrWhiteSpace(prefix))
        {
            prefix = ExpandPlannedProperties(prefix, evaluation.Properties);
            if (evaluation.Properties.TryGetValue("VersionSuffix", out var suffix) && !string.IsNullOrWhiteSpace(suffix))
                return prefix + "-" + ExpandPlannedProperties(suffix, evaluation.Properties);
            return prefix;
        }

        if (evaluation.Properties.TryGetValue("VersionSuffix", out var defaultSuffix) && !string.IsNullOrWhiteSpace(defaultSuffix))
            return "1.0.0-" + ExpandPlannedProperties(defaultSuffix, evaluation.Properties);
        return "1.0.0";
    }

    private static void SetDerivedTargetFrameworkProperties(IDictionary<string, string> properties, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return;
        var framework = NuGetFramework.ParseFolder(targetFramework!);
        if (framework.IsUnsupported)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because target framework '{targetFramework}' is not recognized.");
        properties["TargetFrameworkIdentifier"] = framework.Framework;
        properties["TargetFrameworkVersion"] = "v" + FormatFrameworkVersion(framework.Version);
        properties["TargetFrameworkProfile"] = framework.Profile ?? string.Empty;
        properties["TargetPlatformIdentifier"] = framework.Platform ?? string.Empty;
        properties["TargetPlatformVersion"] = framework.HasPlatform ? FormatFrameworkVersion(framework.PlatformVersion) : string.Empty;
    }

    private static string FormatFrameworkVersion(Version version)
    {
        var fields = version.Revision >= 0
            ? new[] { version.Major, version.Minor, version.Build, version.Revision }
            : version.Build >= 0
                ? new[] { version.Major, version.Minor, version.Build }
                : new[] { version.Major, version.Minor };
        return string.Join(".", fields);
    }

    private static void EvaluatePlannedFile(
        string path,
        string projectPath,
        Dictionary<string, string> properties,
        ICollection<PlannedItem> items,
        IDictionary<string, Dictionary<string, string>> definitions,
        ISet<string> visited,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || !visited.Add(fullPath))
            return;
        var root = plannedProjectContentsByPath is not null && plannedProjectContentsByPath.TryGetValue(fullPath, out var plannedContent)
            ? XDocument.Parse(plannedContent, LoadOptions.PreserveWhitespace).Root
            : XDocument.Load(fullPath, LoadOptions.PreserveWhitespace).Root;
        if (root is null)
            return;

        var directory = Path.GetDirectoryName(fullPath)!;
        var scopedNames = new[] { "MSBuildThisFileDirectory", "MSBuildThisFileFullPath", "MSBuildThisFileName" };
        var previous = scopedNames.ToDictionary(name => name, name => properties.TryGetValue(name, out var value) ? value : null, StringComparer.OrdinalIgnoreCase);
        properties["MSBuildThisFileDirectory"] = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        properties["MSBuildThisFileFullPath"] = fullPath;
        properties["MSBuildThisFileName"] = Path.GetFileNameWithoutExtension(fullPath);
        try
        {
            EvaluatePlannedElements(root.Elements(), projectPath, directory, properties, items, definitions, visited, plannedProjectContentsByPath);
        }
        finally
        {
            foreach (var name in scopedNames)
            {
                if (previous[name] is { } value)
                    properties[name] = value;
                else
                    properties.Remove(name);
            }
        }
    }

    private static void EvaluatePlannedElements(
        IEnumerable<XElement> elements,
        string projectPath,
        string sourceDirectory,
        Dictionary<string, string> properties,
        ICollection<PlannedItem> items,
        IDictionary<string, Dictionary<string, string>> definitions,
        ISet<string> visited,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (var element in elements)
        {
            var name = element.Name.LocalName;
            if (name.Equals("Target", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties, sourceDirectory))
                    continue;
                foreach (var property in element.Elements())
                {
                    var propertyName = property.Name.LocalName;
                    var isSuppliedGlobal =
                        (propertyName.Equals("Configuration", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(properties["Configuration"])) ||
                        (propertyName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(properties["TargetFrameworkIdentifier"]));
                    if (!isSuppliedGlobal && ConditionMatches(property.Attribute("Condition")?.Value, properties, sourceDirectory))
                        properties[property.Name.LocalName] = ExpandPlannedProperties(property.Value.Trim(), properties);
                }
                continue;
            }
            if (name.Equals("ItemDefinitionGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties, sourceDirectory))
                    continue;
                foreach (var definition in element.Elements())
                {
                    if (!ConditionMatches(definition.Attribute("Condition")?.Value, properties, sourceDirectory))
                        continue;
                    if (!definitions.TryGetValue(definition.Name.LocalName, out var metadata))
                    {
                        metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        definitions[definition.Name.LocalName] = metadata;
                    }
                    ApplyPlannedMetadata(definition, metadata, properties, items, sourceDirectory);
                }
                continue;
            }
            if (name.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties, sourceDirectory))
                    continue;
                foreach (var item in element.Elements())
                {
                    if (!ConditionMatches(item.Attribute("Condition")?.Value, properties, sourceDirectory))
                        continue;
                    definitions.TryGetValue(item.Name.LocalName, out var defaults);
                    items.Add(PlannedItem.Create(item, projectDirectory, properties, items, defaults, sourceDirectory));
                }
                continue;
            }
            if (name.Equals("Import", StringComparison.OrdinalIgnoreCase))
            {
                EvaluatePlannedImport(element, projectPath, sourceDirectory, properties, items, definitions, visited, plannedProjectContentsByPath);
                continue;
            }
            if (name.Equals("ImportGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties, sourceDirectory))
                    continue;
                foreach (var import in element.Elements().Where(child => child.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
                    EvaluatePlannedImport(import, projectPath, sourceDirectory, properties, items, definitions, visited, plannedProjectContentsByPath);
                continue;
            }
            if (name.Equals("Choose", StringComparison.OrdinalIgnoreCase))
            {
                var selected = element.Elements().FirstOrDefault(branch => branch.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) && ConditionMatches(branch.Attribute("Condition")?.Value, properties, sourceDirectory))
                               ?? element.Elements().FirstOrDefault(branch => branch.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase));
                if (selected is not null)
                    EvaluatePlannedElements(selected.Elements(), projectPath, sourceDirectory, properties, items, definitions, visited, plannedProjectContentsByPath);
            }
        }
    }

    private static void ApplyPlannedMetadata(
        XElement source,
        IDictionary<string, string> metadata,
        IReadOnlyDictionary<string, string> properties,
        IEnumerable<PlannedItem> items,
        string conditionDirectory)
    {
        foreach (var child in source.Elements())
        {
            if (ConditionMatches(child.Attribute("Condition")?.Value, properties, conditionDirectory))
                metadata[child.Name.LocalName] = ExpandPlannedValue(child.Value.Trim(), properties, items);
        }
    }

    private static void EvaluatePlannedImport(
        XElement import,
        string projectPath,
        string sourceDirectory,
        Dictionary<string, string> properties,
        ICollection<PlannedItem> items,
        IDictionary<string, Dictionary<string, string>> definitions,
        ISet<string> visited,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        if (!ConditionMatches(import.Attribute("Condition")?.Value, properties, sourceDirectory))
            return;
        var importedPath = import.Attribute("Project")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(importedPath))
            return;
        importedPath = ExpandPlannedProperties(importedPath!, properties);
        if (importedPath.IndexOf("$(", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because import '{importedPath}' cannot be resolved without full MSBuild evaluation.");
        var importedPaths = ResolvePlannedImportPaths(sourceDirectory, importedPath);
        if (importedPaths.Count == 0 && importedPath.IndexOfAny(new[] { '*', '?' }) < 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because imported project '{ResolvePlannedPath(sourceDirectory, importedPath)}' does not exist.");
        foreach (var resolvedPath in importedPaths)
            EvaluatePlannedFile(resolvedPath, projectPath, properties, items, definitions, visited, plannedProjectContentsByPath);
    }

    private static IReadOnlyList<string> ResolvePlannedImportPaths(string sourceDirectory, string importedPath)
    {
        if (importedPath.IndexOfAny(new[] { '*', '?' }) < 0)
        {
            var resolved = ResolvePlannedPath(sourceDirectory, importedPath);
            return File.Exists(resolved) ? new[] { resolved } : Array.Empty<string>();
        }

        return ResolvePlannedWildcardPaths(sourceDirectory, importedPath);
    }

    private static IReadOnlyList<string> ResolvePlannedWildcardPaths(string baseDirectory, string pattern)
    {
        var normalized = pattern.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var combinedPattern = Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDirectory, normalized);
        var wildcardIndex = combinedPattern.IndexOfAny(new[] { '*', '?' });
        var separatorIndex = combinedPattern.LastIndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, wildcardIndex);
        var pathRoot = Path.GetPathRoot(combinedPattern) ?? string.Empty;
        var rootPrefix = separatorIndex >= 0
            ? combinedPattern.Substring(0, separatorIndex < pathRoot.Length ? pathRoot.Length : separatorIndex)
            : baseDirectory;
        var searchRoot = Path.GetFullPath(rootPrefix);
        if (!Directory.Exists(searchRoot))
            return Array.Empty<string>();
        var suffix = separatorIndex >= 0 ? combinedPattern.Substring(separatorIndex + 1) : combinedPattern;
        var canonicalPattern = Path.Combine(searchRoot, suffix);
        return Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Where(path => PlannedItemSpecMatches(canonicalPattern, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PlannedItem> ApplyPlannedItemOperations(IEnumerable<PlannedItem> source, string itemType)
    {
        var result = new List<PlannedItem>();
        foreach (var item in source.Where(item => item.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(item.Remove))
            {
                var removals = SplitPlannedItems(item.Remove).ToArray();
                result.RemoveAll(existing => existing.Include is not null && removals.Any(removal => PlannedItemSpecMatches(removal, existing.Include)));
            }
            if (!string.IsNullOrWhiteSpace(item.Update))
            {
                var updates = SplitPlannedItems(item.Update).ToArray();
                for (var index = 0; index < result.Count; index++)
                {
                    if (result[index].Include is { } include && updates.Any(update => PlannedItemSpecMatches(update, include)))
                        result[index] = result[index].WithMetadata(item.Metadata);
                }
            }
            foreach (var include in ExpandPlannedItemIncludes(item, itemType))
            {
                if (!SplitPlannedItems(item.Exclude).Any(exclude => PlannedItemSpecMatches(exclude, include)))
                    result.Add(item.WithInclude(include));
            }
        }
        return result;
    }

    private static IEnumerable<string> ExpandPlannedItemIncludes(PlannedItem item, string itemType)
    {
        foreach (var include in SplitPlannedItems(item.Include))
        {
            if (include.IndexOfAny(new[] { '*', '?' }) < 0 ||
                itemType.Equals("PackageReference", StringComparison.OrdinalIgnoreCase) ||
                itemType.Equals("PackageVersion", StringComparison.OrdinalIgnoreCase))
            {
                yield return include;
                continue;
            }

            foreach (var path in ResolvePlannedWildcardPaths(item.BaseDirectory, include))
                yield return NormalizePlannedItemSpec(FrameworkCompatibility.GetRelativePath(item.BaseDirectory, path));
        }
    }

    private static bool PlannedItemSpecMatches(string pattern, string value)
    {
        var normalizedPattern = NormalizePlannedItemSpec(pattern);
        var normalizedValue = NormalizePlannedItemSpec(value);
        if (normalizedPattern.IndexOfAny(new[] { '*', '?' }) < 0)
            return string.Equals(normalizedPattern, normalizedValue, StringComparison.OrdinalIgnoreCase);
        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(normalizedValue, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizePlannedItemSpec(string value) => value.Trim().Replace('\\', '/');

    private static IEnumerable<string> SplitPlannedItems(string? value)
        => (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0);

    private static string ExpandPlannedValue(string value, IReadOnlyDictionary<string, string> properties, IEnumerable<PlannedItem> items)
    {
        var expanded = ExpandPlannedProperties(value, properties);
        expanded = Regex.Replace(expanded, @"@\((?<name>[A-Za-z_][A-Za-z0-9_.-]*)\)", match =>
        {
            var referenced = ApplyPlannedItemOperations(items, match.Groups["name"].Value);
            return string.Join(";", referenced.Select(item => item.Include).Where(include => !string.IsNullOrWhiteSpace(include)));
        });
        if (expanded.IndexOf("@(", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because item expression '{value}' requires unsupported MSBuild transforms or separators.");
        return expanded;
    }

    private static string ResolvePlannedPath(string directory, string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(directory, normalized));
    }

    private sealed class PlannedEvaluation
    {
        internal PlannedEvaluation(IReadOnlyDictionary<string, string> properties, IReadOnlyList<PlannedItem> items)
        {
            Properties = properties;
            Items = items;
        }
        internal IReadOnlyDictionary<string, string> Properties { get; }
        internal IReadOnlyList<PlannedItem> Items { get; }
    }

    private sealed class PlannedItem
    {
        private PlannedItem(string itemType, string? include, string? exclude, string? update, string? remove, string baseDirectory, IReadOnlyDictionary<string, string> metadata)
        {
            ItemType = itemType;
            Include = include;
            Exclude = exclude;
            Update = update;
            Remove = remove;
            BaseDirectory = baseDirectory;
            Metadata = metadata;
        }
        internal string ItemType { get; }
        internal string? Include { get; }
        internal string? Exclude { get; }
        internal string? Update { get; }
        internal string? Remove { get; }
        internal string BaseDirectory { get; }
        internal IReadOnlyDictionary<string, string> Metadata { get; }
        internal string? GetMetadata(string name) => Metadata.TryGetValue(name, out var value) ? value : null;
        internal PlannedItem WithInclude(string include) => new(ItemType, include, Exclude, null, null, BaseDirectory, Metadata);
        internal PlannedItem WithMetadata(IReadOnlyDictionary<string, string> updates)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Metadata)
                merged[entry.Key] = entry.Value;
            foreach (var entry in updates)
                merged[entry.Key] = entry.Value;
            return new PlannedItem(ItemType, Include, Exclude, null, null, BaseDirectory, merged);
        }
        internal static PlannedItem Create(
            XElement element,
            string baseDirectory,
            IReadOnlyDictionary<string, string> properties,
            IEnumerable<PlannedItem> items,
            IReadOnlyDictionary<string, string>? defaults,
            string conditionDirectory)
        {
            string? ExpandAttribute(string name)
            {
                var value = element.Attribute(name)?.Value?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : ExpandPlannedValue(value!, properties, items);
            }
            var include = ExpandAttribute("Include");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (include is not null && defaults is not null)
            {
                foreach (var entry in defaults)
                    metadata[entry.Key] = entry.Value;
            }
            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (name.Equals("Include", StringComparison.OrdinalIgnoreCase) || name.Equals("Exclude", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Update", StringComparison.OrdinalIgnoreCase) || name.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Condition", StringComparison.OrdinalIgnoreCase))
                    continue;
                metadata[name] = ExpandPlannedValue(attribute.Value.Trim(), properties, items);
            }
            ApplyPlannedMetadata(element, metadata, properties, items, conditionDirectory);
            return new PlannedItem(element.Name.LocalName, include, ExpandAttribute("Exclude"), ExpandAttribute("Update"), ExpandAttribute("Remove"), baseDirectory, metadata);
        }
    }
}
