using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>Preserves project version references when a binding updates their imported MSBuild property.</summary>
internal static partial class BoundProjectVersionService
{
    private const RegexOptions MatchOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex VersionReference = new(
        @"^\s*\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)\s*$",
        MatchOptions, RegexTimeout);

    internal static string PreserveReferences(
        string repositoryRoot,
        string projectPath,
        string originalContent,
        string updatedContent,
        string plannedContent,
        string resolvedVersion,
        IReadOnlyList<ProjectVersionBindingFileUpdate> bindings,
        IReadOnlyList<ProjectVersionBinding>? configuredBindings)
    {
        if (bindings.Count == 0)
            return updatedContent;

        var project = XDocument.Parse(originalContent);
        var composedProject = XDocument.Parse(updatedContent);
        ValidateComposition(projectPath, plannedContent, composedProject, resolvedVersion);
        var originalElements = FindVersionElements(originalContent);
        if (!originalElements.Any(element => VersionReference.IsMatch(element.Element.Value)))
            return updatedContent;

        var propsPath = FindImportedProps(repositoryRoot, projectPath);
        if (propsPath is null)
            return updatedContent;
        var comparison = FrameworkCompatibility.GetPathStringComparison(repositoryRoot);
        var binding = bindings.FirstOrDefault(item =>
            string.Equals(Path.GetFullPath(item.Update.FilePath), propsPath, comparison));
        if (binding is null)
            return updatedContent;

        var props = XDocument.Parse(binding.Update.UpdatedContent);
        if (HasCustomImports(project) || HasImports(props) || HasSdkImports(props))
            return updatedContent;
        var preserved = new Dictionary<int, string>();
        for (var index = 0; index < originalElements.Length; index++)
        {
            var reference = VersionReference.Match(originalElements[index].Element.Value);
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
                .Where(element => IsEvaluationProperty(element) &&
                    string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (definitions.Length != 1 || definitions[0].Parent?.Parent != props.Root || definitions[0].AncestorsAndSelf().Any(element =>
                !string.IsNullOrWhiteSpace((string?)element.Attribute("Condition"))))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' must have one unconditional definition in '{propsPath}'.");
            ValidateVersionProperties(propsPath, props, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { propertyName });
            if (!string.Equals(definitions[0].Value.Trim(), resolvedVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Bound MSBuild version property '{propertyName}' does not match resolved version '{resolvedVersion}' for '{projectPath}'.");

            preserved.Add(index, originalContent.Substring(originalElements[index].Index, originalElements[index].Length));
        }
        if (preserved.Count == 0)
            return updatedContent;

        var updatedElements = FindVersionElements(updatedContent);
        // A project binding may edit other metadata, but adding, removing or moving version
        // consumers makes ordinal restoration ambiguous. Reject that composition atomically.
        var restored = new StringBuilder(updatedContent.Length);
        var offset = 0;
        for (var index = 0; index < updatedElements.Length; index++)
        {
            var element = updatedElements[index];
            restored.Append(updatedContent, offset, element.Index - offset);
            if (preserved.TryGetValue(index, out var reference))
                restored.Append(reference);
            else
                restored.Append(updatedContent, element.Index, element.Length);
            offset = element.Index + element.Length;
        }
        restored.Append(updatedContent, offset, updatedContent.Length - offset);
        return restored.ToString();
    }

    private static bool HasPropertyDefinition(XDocument project, string propertyName) => project.Descendants().Any(element =>
        IsEvaluationProperty(element) &&
        string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase));

    private static MsBuildProjectXml.ElementSpan[] FindVersionElements(string content) =>
        MsBuildProjectXml.FindProperties(content, "Version").Concat(MsBuildProjectXml.FindProperties(content, "PackageVersion")).OrderBy(element => element.Index).ToArray();

    private static bool IsEvaluationProperty(XElement element) => MsBuildProjectXml.IsEvaluationProperty(element);

    private static bool HasImports(XDocument project) => project.Descendants().Any(element =>
        string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase) &&
        (element.Parent == project.Root || (element.Parent?.Name.LocalName == "ImportGroup" && element.Parent.Parent == project.Root)));

    private static bool HasSdkImports(XDocument project) =>
        !string.IsNullOrWhiteSpace((string?)project.Root?.Attribute("Sdk")) ||
        project.Root?.Elements().Any(element => element.Name.LocalName == "Sdk") == true;

    private static bool HasCustomImports(XDocument project) => HasImports(project) ||
        HasPropertyDefinition(project, "DirectoryBuildPropsPath") || HasPropertyDefinition(project, "ImportDirectoryBuildProps") ||
        ((string?)project.Root?.Attribute("Sdk"))?.Split(';').Any(name =>
            !string.Equals(name.Trim(), "Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase)) == true ||
        project.Root?.Elements().Any(element => element.Name.LocalName == "Sdk" &&
            (!string.Equals((string?)element.Attribute("Name"), "Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase) ||
             element.Attributes().Any(attribute => attribute.Name.LocalName != "Name"))) == true;

    private static void ValidateComposition(
        string projectPath, string plannedContent, XDocument updated, string resolvedVersion)
    {
        ValidateVersionProperties(projectPath, updated, VersionPropertyNames);
        static bool IsVersion(XElement element) => IsEvaluationProperty(element) &&
            new[] { "Version", "PackageVersion", "VersionPrefix", "VersionSuffix" }.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase);
        static IEnumerable<string> Location(XElement element) => element.AncestorsAndSelf().Reverse()
            .Select(ancestor => ancestor.Name.LocalName.ToUpperInvariant() + "\0" + (string?)ancestor.Attribute("Condition"));

        // Preserve the actual plan: a release without an expected/aligned version
        // deliberately leaves the project's version ownership unchanged.
        var original = XDocument.Parse(plannedContent);
        var originalElements = original.Descendants().Where(IsVersion).ToArray();
        var updatedElements = updated.Descendants().Where(IsVersion).ToArray();
        if (originalElements.Length != updatedElements.Length ||
            originalElements.Where((element, index) => !Location(element).SequenceEqual(Location(updatedElements[index]), StringComparer.Ordinal)).Any())
            throw new InvalidOperationException($"Project '{projectPath}' version element layout changed during binding composition; bound references cannot be restored safely.");
        for (var index = 0; index < originalElements.Length; index++)
        {
            var plannedValue = originalElements[index].Value.Trim();
            var composedValue = updatedElements[index].Value.Trim();
            if (string.Equals(plannedValue, composedValue, StringComparison.Ordinal) ||
                (plannedValue.Length == 0 &&
                 (originalElements[index].Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase) ||
                  originalElements[index].Name.LocalName.Equals("PackageVersion", StringComparison.OrdinalIgnoreCase)) &&
                 string.Equals(composedValue, resolvedVersion, StringComparison.Ordinal)))
                continue;
            throw new InvalidOperationException($"Updated project version element does not match resolved version '{resolvedVersion}' for '{projectPath}'.");
        }
    }

    private static bool IsPropertyBound(
        string root, string propsPath, string propertyName, string originalProps,
        IReadOnlyList<ProjectVersionBinding>? bindings, StringComparison comparison)
    {
        if (bindings is null)
            return false;
        var propertyElements = MsBuildProjectXml.FindProperties(originalProps, propertyName);
        foreach (var binding in bindings)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(root, binding.Path.Trim())), propsPath, comparison))
                continue;
            var matches = Regex.Matches(originalProps, binding.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            foreach (Match match in matches)
            foreach (var property in propertyElements)
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
