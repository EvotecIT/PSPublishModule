using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>Preserves project version references when a binding updates their imported MSBuild property.</summary>
internal static class BoundProjectVersionService
{
    private const RegexOptions MatchOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex VersionElement = new(
        @"<(?<tag>Version|PackageVersion)>(?<value>[^<]*)</\k<tag>>", MatchOptions, RegexTimeout);
    private static readonly Regex VersionReference = new(
        @"<(?<tag>Version|PackageVersion)>\s*\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)\s*</\k<tag>>",
        MatchOptions, RegexTimeout);

    internal static string PreserveReferences(
        string repositoryRoot,
        string projectPath,
        string originalContent,
        string updatedContent,
        string resolvedVersion,
        IReadOnlyList<ProjectVersionBindingFileUpdate> bindings,
        IReadOnlyList<ProjectVersionBinding>? configuredBindings)
    {
        var originalElements = VersionElement.Matches(originalContent).Cast<Match>().ToArray();
        if (!originalElements.Any(element => VersionReference.IsMatch(element.Value)) || bindings.Count == 0)
            return updatedContent;

        var propsPath = FindImportedProps(repositoryRoot, projectPath);
        if (propsPath is null)
            return updatedContent;
        var comparison = FrameworkCompatibility.GetPathStringComparison(repositoryRoot);
        var binding = bindings.FirstOrDefault(item =>
            string.Equals(Path.GetFullPath(item.Update.FilePath), propsPath, comparison));
        if (binding is null)
            return updatedContent;

        var project = XDocument.Parse(originalContent);
        var composedProject = XDocument.Parse(updatedContent);
        var props = XDocument.Parse(binding.Update.UpdatedContent);
        if (HasCustomImports(project) || HasImports(props))
            return updatedContent;
        var preserved = new Dictionary<int, string>();
        for (var index = 0; index < originalElements.Length; index++)
        {
            var reference = VersionReference.Match(originalElements[index].Value);
            if (!reference.Success)
                continue;
            var propertyName = reference.Groups["property"].Value;
            if (!IsPropertyBound(repositoryRoot, propsPath, propertyName, binding.Update.OriginalContent, configuredBindings, comparison))
                continue;
            // A project-local definition takes precedence over its automatically imported props.
            if (HasPropertyDefinition(project, propertyName))
                continue;
            if (HasCustomImports(composedProject) || HasPropertyDefinition(composedProject, propertyName))
                throw new InvalidOperationException($"Project '{projectPath}' version property ownership changed during binding composition; bound references cannot be restored safely.");

            var definitions = props.Descendants()
                .Where(element => element.Parent?.Name.LocalName == "PropertyGroup" &&
                    string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (definitions.Length != 1 || definitions[0].Parent?.Parent != props.Root || definitions[0].AncestorsAndSelf().Any(element =>
                !string.IsNullOrWhiteSpace((string?)element.Attribute("Condition"))))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' must have one unconditional definition in '{propsPath}'.");
            if (!string.Equals(definitions[0].Value.Trim(), resolvedVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' does not match resolved version '{resolvedVersion}' for '{projectPath}'.");

            preserved.Add(index, reference.Value);
        }
        if (preserved.Count == 0)
            return updatedContent;

        var updatedElements = VersionElement.Matches(updatedContent).Cast<Match>().ToArray();
        // A project binding may edit other metadata, but adding, removing or moving version
        // consumers makes ordinal restoration ambiguous. Reject that composition atomically.
        ValidateVersionLayout(projectPath, project, composedProject, originalElements, updatedElements);
        var restored = new StringBuilder(updatedContent.Length);
        var offset = 0;
        for (var index = 0; index < updatedElements.Length; index++)
        {
            var element = updatedElements[index];
            restored.Append(updatedContent, offset, element.Index - offset);
            // The editor updates both literal consumers and reference consumers. A composed
            // binding must not change either planned value before the file transaction runs.
            if (originalElements[index].Groups["value"].Length > 0 &&
                !string.Equals(element.Groups["value"].Value.Trim(), resolvedVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Updated project version element does not match resolved version '{resolvedVersion}' for '{projectPath}'.");
            if (preserved.TryGetValue(index, out var reference))
                restored.Append(reference);
            else
                restored.Append(element.Value);
            offset = element.Index + element.Length;
        }
        restored.Append(updatedContent, offset, updatedContent.Length - offset);
        return restored.ToString();
    }

    private static bool HasPropertyDefinition(XDocument project, string propertyName) => project.Descendants().Any(element =>
        element.Parent?.Name.LocalName == "PropertyGroup" &&
        string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase));

    private static bool HasImports(XDocument project) => project.Descendants().Any(element =>
        string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase) &&
        (element.Parent == project.Root || element.Parent?.Name.LocalName == "ImportGroup"));

    private static bool HasCustomImports(XDocument project) => HasImports(project) ||
        HasPropertyDefinition(project, "DirectoryBuildPropsPath") || HasPropertyDefinition(project, "ImportDirectoryBuildProps");

    private static void ValidateVersionLayout(
        string projectPath, XDocument original, XDocument updated, Match[] originalMatches, Match[] updatedMatches)
    {
        static bool IsVersion(XElement element) =>
            string.Equals(element.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(element.Name.LocalName, "PackageVersion", StringComparison.OrdinalIgnoreCase);
        static IEnumerable<string> Location(XElement element) => element.AncestorsAndSelf().Reverse()
            .Select(ancestor => ancestor.Name.LocalName.ToUpperInvariant() + "\0" + (string?)ancestor.Attribute("Condition"));

        var originalElements = original.Descendants().Where(IsVersion).ToArray();
        var updatedElements = updated.Descendants().Where(IsVersion).ToArray();
        if (originalElements.Length != updatedElements.Length || originalMatches.Length != updatedMatches.Length ||
            originalElements.Where((element, index) => !Location(element).SequenceEqual(Location(updatedElements[index]), StringComparer.Ordinal)).Any() ||
            originalMatches.Where((element, index) => !string.Equals(element.Groups["tag"].Value,
                updatedMatches[index].Groups["tag"].Value, StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidOperationException($"Project '{projectPath}' version element layout changed during binding composition; bound references cannot be restored safely.");
    }

    private static bool IsPropertyBound(
        string root, string propsPath, string propertyName, string originalProps,
        IReadOnlyList<ProjectVersionBinding>? bindings, StringComparison comparison)
    {
        if (bindings is null)
            return false;
        var propertyStarts = FindPropertyOffsets(originalProps, propertyName);
        var propertyElements = Regex.Matches(originalProps,
            @"<" + Regex.Escape(propertyName) + @"\b[^>]*(?:/\s*>|>[^<]*</" + Regex.Escape(propertyName) + @"\s*>)",
            MatchOptions, RegexTimeout).Cast<Match>().Where(match => propertyStarts.Contains(match.Index)).ToArray();
        foreach (var binding in bindings)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(root, binding.Path.Trim())), propsPath, comparison))
                continue;
            var matches = Regex.Matches(originalProps, binding.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            foreach (Match match in matches)
            foreach (Match property in propertyElements)
            {
                if (match.Index < property.Index + property.Length && match.Index + match.Length > property.Index)
                    return true;
            }
        }
        return false;
    }

    private static HashSet<int> FindPropertyOffsets(string content, string propertyName)
    {
        // XML roles identify actual properties; source offsets retain the binding's
        // text interval without mistaking items, tasks or comments for definitions.
        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\r')
            {
                if (index + 1 < content.Length && content[index + 1] == '\n')
                    index++;
                lineStarts.Add(index + 1);
            }
            else if (content[index] == '\n')
                lineStarts.Add(index + 1);
        }
        var document = XDocument.Parse(content, LoadOptions.SetLineInfo);
        return new HashSet<int>(document.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup" &&
                string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            .Select(element =>
            {
                var position = (IXmlLineInfo)element;
                return lineStarts[position.LineNumber - 1] + position.LinePosition - 2;
            }));
    }

    private static string? FindImportedProps(string repositoryRoot, string projectPath)
    {
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = FrameworkCompatibility.GetPathStringComparison(root);
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
        while (string.Equals(directory.FullName, root, comparison) ||
               directory.FullName.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            var path = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(path))
                return path;
            directory = directory.Parent!;
            if (directory is null)
                break;
        }
        return null;
    }
}
