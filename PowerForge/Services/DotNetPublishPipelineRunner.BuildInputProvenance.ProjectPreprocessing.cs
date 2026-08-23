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
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
        arguments.Add("-p:BuildProjectReferences=false");

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

            StringComparer importComparer = IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var resolved = new List<string>();
            var seenImports = new HashSet<string>(importComparer);
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
                    {
                        string resolvedPath = Path.GetFullPath(candidate);
                        if (seenImports.Add(resolvedPath))
                            resolved.Add(resolvedPath);
                    }
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
            string[] initialTargetExpressions = hasTargetTimeProjectReferences
                ? ReadProjectInitialTargetExpressions(request.ProjectPath, imports)
                : Array.Empty<string>();
            ScheduledProjectReferenceTargetGraph scheduledTargets = hasTargetTimeProjectReferences
                ? ReadScheduledProjectReferenceTargets(request, document, initialTargetExpressions)
                : ScheduledProjectReferenceTargetGraph.Empty;
            IReadOnlyDictionary<string, EvaluatedProjectItem[]> evaluatedItemLists =
                ReadEvaluatedProjectItemPaths(
                    request,
                    ReadProjectReferenceItemListNames(document),
                    executeResolveReferences:
                        RequiresTargetExecutionForProjectReferenceIdentities(document));
            HashSet<string> immutableGlobalProperties = ReadImmutableGlobalPropertyNames(
                request,
                document.Root);
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
                    if (immutableGlobalProperties.Contains(element.Name.LocalName))
                        continue;

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

            IReadOnlyDictionary<string, string> initialProperties =
                BuildInitialProjectReferenceProperties(request);

            projectReferenceDeclarations = declarationElements
                .OrderBy(declaration => declaration.IsTargetTime ? 1 : 0)
                .ThenBy(declaration => declaration.ContainingTarget is null
                    ? -1
                    : scheduledTargets.ReadExecutionIndex(declaration.ContainingTarget))
                .Select(declaration =>
                {
                    PreprocessedProjectPropertyDefinition[] runtimePropertyDefinitions =
                        (declaration.ContainingTarget is null
                            ? Array.Empty<PreprocessedProjectPropertyDefinition>()
                            : scheduledTargets.ReadPredecessors(declaration.ContainingTarget)
                                .SelectMany(target => targetPropertyDefinitions.TryGetValue(
                                    target,
                                    out List<PreprocessedProjectPropertyDefinition>? definitions)
                                        ? (IEnumerable<PreprocessedProjectPropertyDefinition>)definitions
                                        : Array.Empty<PreprocessedProjectPropertyDefinition>()))
                        .Concat(declaration.TargetPropertyDefinitions)
                        .Distinct()
                        .ToArray();
                    return new PreprocessedProjectReferenceDeclaration(
                        declaration.Element,
                        declaration.DefiningProjectPath,
                        propertyDefinitions.Concat(runtimePropertyDefinitions).ToArray(),
                        runtimePropertyDefinitions,
                        initialProperties,
                        evaluatedItemLists,
                        declaration.IsTargetTime,
                        declaration.RunsBeforeResolveReferences);
                })
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
        XDocument document,
        IReadOnlyCollection<string> initialTargetExpressions)
    {
        var effectiveTargets = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement target in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value)))
        {
            effectiveTargets[target.Attribute("Name")!.Value.Trim()] = target;
        }

        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string?> targetExpressions = effectiveTargets.Values.SelectMany(target => new[]
            {
                target.Attribute("DependsOnTargets")?.Value,
                target.Attribute("BeforeTargets")?.Value,
                target.Attribute("AfterTargets")?.Value
            })
            .Concat(initialTargetExpressions);
        foreach (string expression in targetExpressions.Where(value => !string.IsNullOrWhiteSpace(value))!)
        {
            AddConditionPropertyNames(expression, propertyNames);
        }

        IReadOnlyDictionary<string, string> evaluatedProperties =
            ReadEvaluatedProjectProperties(request, propertyNames);
        var initialExecutionProperties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> property in evaluatedProperties)
            initialExecutionProperties[property.Key] = property.Value;
        var executionPropertyContexts = new List<Dictionary<string, string>>
        {
            initialExecutionProperties
        };
        HashSet<string> immutableGlobalProperties = ReadImmutableGlobalPropertyNames(
            request,
            document.Root!);
        XElement[] effectiveTargetOrder = document.Descendants().Where(element =>
                element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value) &&
                effectiveTargets.TryGetValue(
                    element.Attribute("Name")!.Value.Trim(),
                    out XElement? effective) &&
                ReferenceEquals(element, effective))
            .ToArray();
        var beforeTargets = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        var afterTargets = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement target in effectiveTargetOrder)
        {
            foreach (string destination in ReadExpandedMsBuildTargetList(
                         target.Attribute("BeforeTargets")?.Value,
                         evaluatedProperties))
            {
                AddTargetHook(beforeTargets, destination, target);
            }

            foreach (string destination in ReadExpandedMsBuildTargetList(
                         target.Attribute("AfterTargets")?.Value,
                         evaluatedProperties))
            {
                AddTargetHook(afterTargets, destination, target);
            }
        }

        if (!effectiveTargets.TryGetValue("ResolveReferences", out XElement? resolveReferences))
            return ScheduledProjectReferenceTargetGraph.Empty;

        var executionOrder = new List<XElement>();
        var visiting = new HashSet<XElement>();
        var executed = new HashSet<XElement>();
        foreach (string initialTargetName in initialTargetExpressions.SelectMany(expression =>
                     ReadExpandedMsBuildTargetList(expression, evaluatedProperties)))
        {
            if (effectiveTargets.TryGetValue(initialTargetName, out XElement? initialTarget))
            {
                AddTargetExecutionOrder(
                    initialTarget,
                    effectiveTargets,
                    beforeTargets,
                    afterTargets,
                    evaluatedProperties,
                    executionPropertyContexts,
                    immutableGlobalProperties,
                    propertyNames,
                    visiting,
                    executed,
                    executionOrder);
            }
        }
        AddTargetExecutionOrder(
            resolveReferences,
            effectiveTargets,
            beforeTargets,
            afterTargets,
            evaluatedProperties,
            executionPropertyContexts,
            immutableGlobalProperties,
            propertyNames,
            visiting,
            executed,
            executionOrder);
        int resolveReferencesIndex = executionOrder.IndexOf(resolveReferences);
        return resolveReferencesIndex < 0
            ? ScheduledProjectReferenceTargetGraph.Empty
            : new ScheduledProjectReferenceTargetGraph(
                executionOrder.Take(resolveReferencesIndex + 1).ToArray());
    }

    private static void AddTargetExecutionOrder(
        XElement target,
        IReadOnlyDictionary<string, XElement> effectiveTargets,
        IReadOnlyDictionary<string, List<XElement>> beforeTargets,
        IReadOnlyDictionary<string, List<XElement>> afterTargets,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        List<Dictionary<string, string>> executionPropertyContexts,
        ISet<string> immutableGlobalProperties,
        ISet<string> scheduledPropertyNames,
        HashSet<XElement> visiting,
        HashSet<XElement> executed,
        List<XElement> executionOrder)
    {
        if (executed.Contains(target) || !visiting.Add(target))
            return;

        foreach (string dependency in executionPropertyContexts.SelectMany(context =>
                     ReadExpandedMsBuildTargetList(
                         target.Attribute("DependsOnTargets")?.Value,
                         context)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (effectiveTargets.TryGetValue(dependency, out XElement? dependencyTarget))
            {
                AddTargetExecutionOrder(
                    dependencyTarget,
                    effectiveTargets,
                    beforeTargets,
                    afterTargets,
                    evaluatedProperties,
                    executionPropertyContexts,
                    immutableGlobalProperties,
                    scheduledPropertyNames,
                    visiting,
                    executed,
                    executionOrder);
            }
        }

        string targetName = target.Attribute("Name")!.Value.Trim();
        if (beforeTargets.TryGetValue(targetName, out List<XElement>? beforeHooks))
        {
            foreach (XElement hook in beforeHooks)
            {
                AddTargetExecutionOrder(
                    hook,
                    effectiveTargets,
                    beforeTargets,
                    afterTargets,
                    evaluatedProperties,
                    executionPropertyContexts,
                    immutableGlobalProperties,
                    scheduledPropertyNames,
                    visiting,
                    executed,
                    executionOrder);
            }
        }

        visiting.Remove(target);
        if (executed.Add(target))
        {
            executionOrder.Add(target);
            ApplyScheduledTargetPropertyDefinitions(
                target,
                executionPropertyContexts,
                immutableGlobalProperties,
                scheduledPropertyNames);
        }

        if (afterTargets.TryGetValue(targetName, out List<XElement>? afterHooks))
        {
            foreach (XElement hook in afterHooks)
            {
                AddTargetExecutionOrder(
                    hook,
                    effectiveTargets,
                    beforeTargets,
                    afterTargets,
                    evaluatedProperties,
                    executionPropertyContexts,
                    immutableGlobalProperties,
                    scheduledPropertyNames,
                    visiting,
                    executed,
                    executionOrder);
            }
        }
    }

    private static void AddTargetHook(
        IDictionary<string, List<XElement>> hooks,
        string destination,
        XElement target)
    {
        if (!hooks.TryGetValue(destination, out List<XElement>? targets))
        {
            targets = new List<XElement>();
            hooks[destination] = targets;
        }
        if (!targets.Contains(target))
            targets.Add(target);
    }

    private sealed class ScheduledProjectReferenceTargetGraph
    {
        internal static ScheduledProjectReferenceTargetGraph Empty { get; } = new(
            Array.Empty<XElement>());

        private readonly XElement[] _executionOrder;
        private readonly IReadOnlyDictionary<XElement, int> _executionIndexes;

        internal ScheduledProjectReferenceTargetGraph(XElement[] executionOrder)
        {
            _executionOrder = executionOrder;
            _executionIndexes = executionOrder
                .Select((target, index) => new { Target = target, Index = index })
                .ToDictionary(entry => entry.Target, entry => entry.Index);
        }

        internal bool Contains(XElement target) => _executionIndexes.ContainsKey(target);

        internal int ReadExecutionIndex(XElement target)
            => _executionIndexes.TryGetValue(target, out int index)
                ? index
                : int.MaxValue;

        internal IEnumerable<XElement> ReadPredecessors(XElement target)
        {
            return _executionIndexes.TryGetValue(target, out int index)
                ? _executionOrder.Take(index)
                : Array.Empty<XElement>();
        }
    }

}
