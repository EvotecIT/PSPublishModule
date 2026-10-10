using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal static partial class BoundProjectVersionService
{
    private static readonly HashSet<string> VersionPropertyNames = new(StringComparer.OrdinalIgnoreCase)
        { "Version", "PackageVersion", "VersionPrefix", "VersionSuffix" };

    internal static void ValidatePropertyBindings(
        string repositoryRoot, IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyList<ProjectVersionBindingFileUpdate> bindings)
    {
        if (bindings.Count == 0)
            return;
        var paths = new Dictionary<string, HashSet<string>>(FrameworkCompatibility.PathComparer);
        foreach (var project in projects)
        {
            var path = Path.GetFullPath(project.CsprojPath);
            var props = FindImportedProps(repositoryRoot, project.CsprojPath);
            var documents = ReadComposedDocuments(path, bindings).ToList();
            if (props is not null)
                documents.AddRange(ReadComposedDocuments(props, bindings));
            var names = new HashSet<string>(VersionPropertyNames, StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(names);
            // Include suppliers from both original and planned contents, whether
            // the version is inherited, locally declared, or composed in stages.
            while (pending.Count != 0)
            {
                var name = pending.Dequeue();
                foreach (var element in documents.SelectMany(document => document.Descendants()).Where(element =>
                    IsEvaluationProperty(element) && element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                foreach (Match reference in Regex.Matches(element.Value, @"\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)", MatchOptions, RegexTimeout))
                    if (names.Add(reference.Groups["property"].Value))
                        pending.Enqueue(reference.Groups["property"].Value);
            }
            foreach (var source in props is null ? new[] { path } : new[] { path, props })
            {
                if (!paths.TryGetValue(source, out var sourceNames))
                    paths.Add(source, sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                sourceNames.UnionWith(names);
            }
        }
        foreach (var binding in bindings)
            if (paths.TryGetValue(Path.GetFullPath(binding.Update.FilePath), out var names))
                ValidateVersionProperties(binding.Update.FilePath, XDocument.Parse(binding.Update.UpdatedContent), names);
    }

    private static IEnumerable<XDocument> ReadComposedDocuments(string path, IReadOnlyList<ProjectVersionBindingFileUpdate> bindings)
    {
        var binding = bindings.FirstOrDefault(candidate => FrameworkCompatibility.PathComparer.Equals(
            Path.GetFullPath(candidate.Update.FilePath), path));
        if (binding is null)
            yield return XDocument.Parse(File.ReadAllText(path));
        else
        {
            yield return XDocument.Parse(binding.Update.OriginalContent);
            yield return XDocument.Parse(binding.Update.UpdatedContent);
        }
    }

    private static void ValidateVersionProperties(string path, XDocument document, ISet<string> names)
    {
        // MSBuild permits XML in general property values. Package versions and
        // the evaluated properties supplying them must remain scalar instead.
        var nested = document.Descendants().FirstOrDefault(element => IsEvaluationProperty(element) &&
            names.Contains(element.Name.LocalName) && element.HasElements);
        if (nested is not null)
            throw new InvalidOperationException($"MSBuild version property '{nested.Name.LocalName}' in '{path}' must have a scalar value; child elements are not supported for package versions.");
    }

}
