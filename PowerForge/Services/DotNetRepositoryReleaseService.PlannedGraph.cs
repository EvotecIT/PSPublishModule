using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static PlannedEvaluation EvaluatePlannedProject(string projectPath, string? configuration, string? targetFramework)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration ?? string.Empty,
            ["TargetFramework"] = targetFramework ?? string.Empty,
            ["MSBuildProjectDirectory"] = projectDirectory,
            ["MSBuildProjectFullPath"] = fullProjectPath,
            ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(fullProjectPath)
        };
        var items = new List<PlannedItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(projectDirectory);

        var directoryPackagesProps = FindNearestBuildFile(directory, "Directory.Packages.props");
        var directoryBuildProps = FindNearestBuildFile(directory, "Directory.Build.props");
        var directoryBuildTargets = FindNearestBuildFile(directory, "Directory.Build.targets");
        if (directoryPackagesProps is not null)
            EvaluatePlannedFile(directoryPackagesProps, fullProjectPath, properties, items, visited);
        if (directoryBuildProps is not null)
            EvaluatePlannedFile(directoryBuildProps, fullProjectPath, properties, items, visited);
        EvaluatePlannedFile(fullProjectPath, fullProjectPath, properties, items, visited);
        if (directoryBuildTargets is not null)
            EvaluatePlannedFile(directoryBuildTargets, fullProjectPath, properties, items, visited);

        return new PlannedEvaluation(properties, items);
    }

    private static void EvaluatePlannedFile(
        string path,
        string projectPath,
        Dictionary<string, string> properties,
        ICollection<PlannedItem> items,
        ISet<string> visited)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || !visited.Add(fullPath))
            return;

        var document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
            return;

        var directory = Path.GetDirectoryName(fullPath)!;
        var scopedNames = new[] { "MSBuildThisFileDirectory", "MSBuildThisFileFullPath", "MSBuildThisFileName" };
        var previous = scopedNames.ToDictionary(
            name => name,
            name => properties.TryGetValue(name, out var value) ? value : null,
            StringComparer.OrdinalIgnoreCase);
        properties["MSBuildThisFileDirectory"] = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        properties["MSBuildThisFileFullPath"] = fullPath;
        properties["MSBuildThisFileName"] = Path.GetFileNameWithoutExtension(fullPath);

        try
        {
            EvaluatePlannedElements(root.Elements(), projectPath, directory, properties, items, visited);
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
        ISet<string> visited)
    {
        foreach (var element in elements)
        {
            var name = element.Name.LocalName;
            if (name.Equals("Target", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties))
                    continue;
                foreach (var property in element.Elements())
                {
                    if (ConditionMatches(property.Attribute("Condition")?.Value, properties))
                        properties[property.Name.LocalName] = ExpandPlannedProperties(property.Value.Trim(), properties);
                }
                continue;
            }

            if (name.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties))
                    continue;
                foreach (var item in element.Elements())
                {
                    if (ConditionMatches(item.Attribute("Condition")?.Value, properties))
                        items.Add(PlannedItem.Create(item, sourceDirectory, properties));
                }
                continue;
            }

            if (name.Equals("Import", StringComparison.OrdinalIgnoreCase))
            {
                EvaluatePlannedImport(element, projectPath, sourceDirectory, properties, items, visited);
                continue;
            }

            if (name.Equals("ImportGroup", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConditionMatches(element.Attribute("Condition")?.Value, properties))
                    continue;
                foreach (var import in element.Elements().Where(child => child.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
                    EvaluatePlannedImport(import, projectPath, sourceDirectory, properties, items, visited);
                continue;
            }

            if (name.Equals("Choose", StringComparison.OrdinalIgnoreCase))
            {
                var selected = element.Elements().FirstOrDefault(branch =>
                    branch.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) &&
                    ConditionMatches(branch.Attribute("Condition")?.Value, properties)) ??
                    element.Elements().FirstOrDefault(branch => branch.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase));
                if (selected is not null)
                    EvaluatePlannedElements(selected.Elements(), projectPath, sourceDirectory, properties, items, visited);
            }
        }
    }

    private static void EvaluatePlannedImport(
        XElement import,
        string projectPath,
        string sourceDirectory,
        Dictionary<string, string> properties,
        ICollection<PlannedItem> items,
        ISet<string> visited)
    {
        if (!ConditionMatches(import.Attribute("Condition")?.Value, properties))
            return;
        var importedPath = import.Attribute("Project")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(importedPath))
            return;
        importedPath = ExpandPlannedProperties(importedPath!, properties);
        if (importedPath.IndexOf("$(", StringComparison.Ordinal) >= 0 || importedPath.IndexOfAny(new[] { '*', '?' }) >= 0)
            return;
        var candidate = ResolvePlannedPath(sourceDirectory, importedPath);
        EvaluatePlannedFile(candidate, projectPath, properties, items, visited);
    }

    private static IReadOnlyList<PlannedItem> ApplyPlannedItemOperations(IEnumerable<PlannedItem> source, string itemType)
    {
        var result = new List<PlannedItem>();
        foreach (var item in source.Where(item => item.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(item.Remove))
            {
                var removals = new HashSet<string>(SplitPlannedItems(item.Remove), StringComparer.OrdinalIgnoreCase);
                result.RemoveAll(existing => existing.Include is not null && removals.Contains(existing.Include));
            }
            if (!string.IsNullOrWhiteSpace(item.Update))
            {
                var updates = new HashSet<string>(SplitPlannedItems(item.Update), StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < result.Count; index++)
                {
                    var existingInclude = result[index].Include;
                    if (existingInclude is not null && updates.Contains(existingInclude))
                        result[index] = result[index].WithMetadata(item.Metadata);
                }
            }
            foreach (var include in SplitPlannedItems(item.Include))
                result.Add(item.WithInclude(include));
        }
        return result;
    }

    private static IEnumerable<string> SplitPlannedItems(string? value)
        => (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0);

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
        private PlannedItem(string itemType, string? include, string? update, string? remove, string sourceDirectory, IReadOnlyDictionary<string, string> metadata)
        {
            ItemType = itemType;
            Include = include;
            Update = update;
            Remove = remove;
            SourceDirectory = sourceDirectory;
            Metadata = metadata;
        }

        internal string ItemType { get; }
        internal string? Include { get; }
        internal string? Update { get; }
        internal string? Remove { get; }
        internal string SourceDirectory { get; }
        internal IReadOnlyDictionary<string, string> Metadata { get; }

        internal string? GetMetadata(string name) => Metadata.TryGetValue(name, out var value) ? value : null;

        internal PlannedItem WithInclude(string include) => new(ItemType, include, null, null, SourceDirectory, Metadata);

        internal PlannedItem WithMetadata(IReadOnlyDictionary<string, string> updates)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Metadata)
                merged[entry.Key] = entry.Value;
            foreach (var entry in updates)
                merged[entry.Key] = entry.Value;
            return new PlannedItem(ItemType, Include, null, null, SourceDirectory, merged);
        }

        internal static PlannedItem Create(XElement element, string sourceDirectory, IReadOnlyDictionary<string, string> properties)
        {
            string? ExpandAttribute(string name)
            {
                var value = element.Attribute(name)?.Value?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : ExpandPlannedProperties(value!, properties);
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (name.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Condition", StringComparison.OrdinalIgnoreCase))
                    continue;
                metadata[name] = ExpandPlannedProperties(attribute.Value.Trim(), properties);
            }
            foreach (var child in element.Elements())
                metadata[child.Name.LocalName] = ExpandPlannedProperties(child.Value.Trim(), properties);

            return new PlannedItem(
                element.Name.LocalName,
                ExpandAttribute("Include"),
                ExpandAttribute("Update"),
                ExpandAttribute("Remove"),
                sourceDirectory,
                metadata);
        }
    }
}
