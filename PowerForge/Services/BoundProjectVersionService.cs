using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>Preserves project version references when a binding updates their imported MSBuild property.</summary>
internal static class BoundProjectVersionService
{
    private static readonly Regex VersionReference = new(
        @"<(?<tag>Version|PackageVersion)>\s*\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)\s*</\k<tag>>",
        RegexOptions.CultureInvariant);

    internal static string PreserveReferences(
        string repositoryRoot,
        string projectPath,
        string originalContent,
        string updatedContent,
        string resolvedVersion,
        IReadOnlyList<ProjectVersionBindingFileUpdate> bindings,
        IReadOnlyList<ProjectVersionBinding>? configuredBindings)
    {
        var references = VersionReference.Matches(originalContent).Cast<Match>().ToArray();
        if (references.Length == 0 || bindings.Count == 0)
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
        var props = XDocument.Parse(binding.Update.UpdatedContent);
        if (project.Descendants().Any(element => element.Name.LocalName is
                "Import" or "DirectoryBuildPropsPath" or "ImportDirectoryBuildProps") ||
            props.Descendants().Any(element => element.Name.LocalName == "Import"))
            return updatedContent;
        foreach (var reference in references)
        {
            var propertyName = reference.Groups["property"].Value;
            if (!IsPropertyBound(repositoryRoot, propsPath, propertyName, binding.Update.OriginalContent, configuredBindings, comparison))
                continue;
            // A project-local definition takes precedence over its automatically imported props.
            if (project.Descendants().Any(element =>
                string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var definitions = props.Root?.Elements()
                .Where(group => group.Name.LocalName == "PropertyGroup")
                .SelectMany(group => group.Elements())
                .Where(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<XElement>();
            if (definitions.Length == 0)
                continue;
            if (definitions.Length != 1 || definitions[0].AncestorsAndSelf().Any(element =>
                !string.IsNullOrWhiteSpace((string?)element.Attribute("Condition"))))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' must have one unconditional definition in '{propsPath}'.");
            if (!string.Equals(definitions[0].Value.Trim(), resolvedVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' does not match resolved version '{resolvedVersion}' for '{projectPath}'.");

            var tag = reference.Groups["tag"].Value;
            var elementPattern = @"<" + tag + @">[^<]*</" + tag + @">";
            if (Regex.Matches(originalContent, elementPattern, RegexOptions.CultureInvariant).Count != 1)
                throw new InvalidOperationException($"Project '{projectPath}' must have one '{tag}' element to preserve its bound version reference.");
            updatedContent = Regex.Replace(updatedContent, elementPattern, _ => reference.Value, RegexOptions.CultureInvariant);
        }
        return updatedContent;
    }

    private static bool IsPropertyBound(
        string root, string propsPath, string propertyName, string originalProps,
        IReadOnlyList<ProjectVersionBinding>? bindings, StringComparison comparison)
    {
        if (bindings is null)
            return false;
        var propertyElements = Regex.Matches(originalProps,
            @"<" + Regex.Escape(propertyName) + @"\b[^>]*>[^<]*</" + Regex.Escape(propertyName) + @">",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        foreach (var binding in bindings)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(root, binding.Path)), propsPath, comparison))
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
