using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadPreprocessedProjectImports(
        ProjectEvaluationRequest request,
        out string[] imports,
        out PreprocessedProjectReferenceDeclaration[] projectReferenceDeclarations)
    {
        imports = Array.Empty<string>();
        projectReferenceDeclarations = Array.Empty<PreprocessedProjectReferenceDeclaration>();
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            "powerforge-msbuild-imports-" + Guid.NewGuid().ToString("N") + ".xml");
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-preprocess:" + outputPath
        };
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        if (request.TargetFramework is not null)
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut || !File.Exists(outputPath))
                return false;

            var resolved = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            bool inComment = false;
            bool describesImport = false;
            foreach (string line in File.ReadLines(outputPath))
            {
                if (line.Contains("<!--", StringComparison.Ordinal))
                {
                    inComment = true;
                    describesImport = false;
                }
                if (inComment && line.Contains("<Import", StringComparison.Ordinal))
                    describesImport = true;
                if (inComment && describesImport)
                {
                    string candidate = line.Trim();
                    if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                        resolved.Add(Path.GetFullPath(candidate));
                }
                if (line.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = false;
                    describesImport = false;
                }
            }
            imports = resolved.ToArray();

            XDocument document = XDocument.Load(outputPath, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
                return false;

            bool hasTargetTimeProjectReferences = document.Descendants().Any(element =>
                element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase) &&
                element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)));
            ScheduledProjectReferenceTargetGraph scheduledTargets = hasTargetTimeProjectReferences
                ? ReadScheduledProjectReferenceTargets(request, document)
                : ScheduledProjectReferenceTargetGraph.Empty;
            var declarationSources = new Stack<string>();
            declarationSources.Push(Path.GetFullPath(request.ProjectPath));
            var propertyDefinitions = new List<PreprocessedProjectPropertyDefinition>();
            var targetPropertyDefinitions = new Dictionary<XElement, List<PreprocessedProjectPropertyDefinition>>();
            var declarationElements = new List<(
                XElement Element,
                string DefiningProjectPath,
                XElement? ContainingTarget,
                IReadOnlyList<PreprocessedProjectPropertyDefinition> TargetPropertyDefinitions,
                bool IsTargetTime,
                bool RunsBeforeResolveReferences)>();
            foreach (XNode node in document.Root.DescendantNodes())
            {
                if (node is XComment comment)
                {
                    if (comment.Value.Contains("</Import>", StringComparison.Ordinal))
                    {
                        if (declarationSources.Count > 1)
                            declarationSources.Pop();
                        continue;
                    }

                    if (comment.Value.Contains("<Import", StringComparison.Ordinal) &&
                        TryReadPreprocessedImportPath(comment.Value, out string? importedPath))
                    {
                        declarationSources.Push(importedPath!);
                    }
                    continue;
                }

                if (node is not XElement element)
                    continue;

                XElement? containingTarget = element.Ancestors().FirstOrDefault(ancestor =>
                    ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
                bool isTargetTime = containingTarget is not null;
                bool runsBeforeResolveReferences =
                    !string.IsNullOrEmpty(request.TargetFramework) &&
                    containingTarget is not null &&
                    scheduledTargets.Contains(containingTarget);

                if (element.Parent?.Name.LocalName.Equals(
                        "PropertyGroup",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    var definition = new PreprocessedProjectPropertyDefinition(
                        element,
                        declarationSources.Peek());
                    if (!isTargetTime)
                    {
                        propertyDefinitions.Add(definition);
                    }
                    else if (runsBeforeResolveReferences)
                    {
                        if (!targetPropertyDefinitions.TryGetValue(
                                containingTarget!,
                                out List<PreprocessedProjectPropertyDefinition>? definitions))
                        {
                            definitions = new List<PreprocessedProjectPropertyDefinition>();
                            targetPropertyDefinitions[containingTarget!] = definitions;
                        }
                        definitions.Add(definition);
                    }
                }

                if (element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase) &&
                    (element.Parent?.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) == true ||
                     element.Parent?.Name.LocalName.Equals("ItemDefinitionGroup", StringComparison.OrdinalIgnoreCase) == true))
                {
                    declarationElements.Add((
                        element,
                        declarationSources.Peek(),
                        containingTarget,
                        containingTarget is not null &&
                        targetPropertyDefinitions.TryGetValue(
                            containingTarget,
                            out List<PreprocessedProjectPropertyDefinition>? definitions)
                            ? definitions.ToArray()
                            : Array.Empty<PreprocessedProjectPropertyDefinition>(),
                        isTargetTime,
                        runsBeforeResolveReferences));
                }
            }

            projectReferenceDeclarations = declarationElements
                .Select(declaration => new PreprocessedProjectReferenceDeclaration(
                    declaration.Element,
                    declaration.DefiningProjectPath,
                    propertyDefinitions
                        .Concat(declaration.ContainingTarget is null
                            ? Array.Empty<PreprocessedProjectPropertyDefinition>()
                            : scheduledTargets.ReadPredecessors(declaration.ContainingTarget)
                                .SelectMany(target => targetPropertyDefinitions.TryGetValue(
                                    target,
                                    out List<PreprocessedProjectPropertyDefinition>? definitions)
                                        ? (IEnumerable<PreprocessedProjectPropertyDefinition>)definitions
                                        : Array.Empty<PreprocessedProjectPropertyDefinition>()))
                        .Concat(declaration.TargetPropertyDefinitions)
                        .Distinct()
                        .ToArray(),
                    declaration.IsTargetTime,
                    declaration.RunsBeforeResolveReferences))
                .ToArray();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // The provenance result already fails closed if preprocessing did not complete.
            }
        }
    }

    private static ScheduledProjectReferenceTargetGraph ReadScheduledProjectReferenceTargets(
        ProjectEvaluationRequest request,
        XDocument document)
    {
        var effectiveTargets = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement target in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value)))
        {
            effectiveTargets[target.Attribute("Name")!.Value.Trim()] = target;
        }

        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string expression in effectiveTargets.Values.SelectMany(target => new[]
                 {
                     target.Attribute("DependsOnTargets")?.Value,
                     target.Attribute("BeforeTargets")?.Value
                 }).Where(value => !string.IsNullOrWhiteSpace(value))!)
        {
            AddConditionPropertyNames(expression, propertyNames);
        }

        IReadOnlyDictionary<string, string> evaluatedProperties =
            ReadEvaluatedProjectProperties(request, propertyNames);
        var predecessors = effectiveTargets.Values.ToDictionary(
            target => target,
            _ => new List<XElement>());
        foreach (XElement target in effectiveTargets.Values)
        {
            foreach (string dependency in ReadExpandedMsBuildTargetList(
                         target.Attribute("DependsOnTargets")?.Value,
                         evaluatedProperties))
            {
                if (effectiveTargets.TryGetValue(dependency, out XElement? dependencyTarget))
                    AddDistinctTarget(predecessors[target], dependencyTarget);
            }

            foreach (string destination in ReadExpandedMsBuildTargetList(
                         target.Attribute("BeforeTargets")?.Value,
                         evaluatedProperties))
            {
                if (effectiveTargets.TryGetValue(destination, out XElement? destinationTarget))
                    AddDistinctTarget(predecessors[destinationTarget], target);
            }
        }

        if (!effectiveTargets.TryGetValue("ResolveReferences", out XElement? resolveReferences))
            return ScheduledProjectReferenceTargetGraph.Empty;

        var scheduled = new HashSet<XElement>();
        AddScheduledTargetAndPredecessors(resolveReferences, predecessors, scheduled);
        return new ScheduledProjectReferenceTargetGraph(scheduled, predecessors);
    }

    private static void AddScheduledTargetAndPredecessors(
        XElement target,
        IReadOnlyDictionary<XElement, List<XElement>> predecessors,
        HashSet<XElement> scheduled)
    {
        if (!scheduled.Add(target) || !predecessors.TryGetValue(target, out List<XElement>? direct))
            return;

        foreach (XElement predecessor in direct)
            AddScheduledTargetAndPredecessors(predecessor, predecessors, scheduled);
    }

    private static void AddDistinctTarget(List<XElement> targets, XElement target)
    {
        if (!targets.Contains(target))
            targets.Add(target);
    }

    private sealed class ScheduledProjectReferenceTargetGraph
    {
        internal static ScheduledProjectReferenceTargetGraph Empty { get; } = new(
            new HashSet<XElement>(),
            new Dictionary<XElement, List<XElement>>());

        private readonly HashSet<XElement> _scheduled;
        private readonly IReadOnlyDictionary<XElement, List<XElement>> _predecessors;

        internal ScheduledProjectReferenceTargetGraph(
            HashSet<XElement> scheduled,
            IReadOnlyDictionary<XElement, List<XElement>> predecessors)
        {
            _scheduled = scheduled;
            _predecessors = predecessors;
        }

        internal bool Contains(XElement target) => _scheduled.Contains(target);

        internal IEnumerable<XElement> ReadPredecessors(XElement target)
        {
            var visited = new HashSet<XElement>();
            var ordered = new List<XElement>();
            AddOrderedPredecessors(target, target, visited, ordered);
            return ordered;
        }

        private void AddOrderedPredecessors(
            XElement root,
            XElement target,
            HashSet<XElement> visited,
            List<XElement> ordered)
        {
            if (!visited.Add(target))
                return;

            if (_predecessors.TryGetValue(target, out List<XElement>? direct))
            {
                foreach (XElement predecessor in direct)
                    AddOrderedPredecessors(root, predecessor, visited, ordered);
            }

            if (!ReferenceEquals(target, root) && _scheduled.Contains(target))
                ordered.Add(target);
        }
    }

    private static IEnumerable<string> ReadExpandedMsBuildTargetList(
        string? value,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        string expanded = Regex.Replace(
            value!,
            @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
            match => evaluatedProperties.TryGetValue(match.Groups[1].Value, out string? propertyValue)
                ? propertyValue
                : match.Value,
            RegexOptions.CultureInvariant);
        return expanded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0 && entry.IndexOf("$(", StringComparison.Ordinal) < 0)
            .ToArray();
    }

    private static bool TryReadPreprocessedImportPath(string comment, out string? importPath)
    {
        importPath = comment
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(candidate => Path.IsPathRooted(candidate) && File.Exists(candidate));
        if (importPath is null)
            return false;

        importPath = Path.GetFullPath(importPath);
        return true;
    }
}
