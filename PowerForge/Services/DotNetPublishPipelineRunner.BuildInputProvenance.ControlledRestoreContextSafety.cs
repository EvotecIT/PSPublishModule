using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly HashSet<string> ControlledContextPathProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath", "IntermediateOutputPath",
        "ProjectAssetsFile", "BaseOutputPath", "OutputPath", "OutDir", "TargetDir",
        "TargetPath", "PublishDir",
        "DefaultItemExcludes", "DirectoryBuildPropsPath"
    };

    private static bool TryValidateControlledRestoreContextSources(
        IReadOnlyCollection<ControlledPublishGraphNode> graphNodes,
        ProjectEvaluationRequest rootRequest,
        string originalGitRoot,
        IReadOnlyCollection<EvaluatedProjectReference> rootProjectReferences,
        IReadOnlyCollection<string> rootEvaluatedImports,
        IReadOnlyDictionary<string, string> rootEvaluatedProperties,
        IReadOnlyCollection<VerifiedPackageInputCatalog> verifiedPackageCatalogs,
        out string? failureReason)
    {
        failureReason = null;
        var isolatedProjects = graphNodes.Select(node => node.Request)
            .Append(rootRequest)
            .GroupBy(request => Path.GetFullPath(request.ProjectPath),
                FileSystemPathSafety.ExistingPathComparer)
            .Where(group => group.Select(BuildControlledRestoreContextKey)
                .Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(FileSystemPathSafety.ExistingPathComparer);

        // Reference metadata can replace or remove the command-line props wrapper
        // before the child is evaluated. A selected isolated child must inherit it.
        foreach ((IReadOnlyDictionary<string, string> properties,
                  IEnumerable<EvaluatedProjectReference> references) in
                 new[] { (rootEvaluatedProperties, rootProjectReferences.AsEnumerable()) }
                     .Concat(graphNodes.Select(node =>
                         (node.EvaluatedProperties, node.ProjectReferences.AsEnumerable()))))
        {
            bool referencesIsolatedProject = references.Any(reference =>
                isolatedProjects.Contains(Path.GetFullPath(reference.ProjectPath)));
            if (!referencesIsolatedProject)
                continue;
            if (properties.TryGetValue("_GlobalPropertiesToRemoveFromProjectReferences", out string? removals) &&
                RemovesControlledWrapperPath(removals))
            {
                failureReason = "project reference property removals include DirectoryBuildPropsPath, so the controlled wrapper cannot be inherited safely.";
                return false;
            }
            foreach (EvaluatedProjectReference reference in references)
            {
                if (!isolatedProjects.Contains(Path.GetFullPath(reference.ProjectPath)) ||
                    (!reference.GlobalProperties.ContainsKey("DirectoryBuildPropsPath") &&
                     !reference.UndefineProperties.Contains("DirectoryBuildPropsPath",
                         StringComparer.OrdinalIgnoreCase)))
                    continue;
                failureReason = $"project reference changes DirectoryBuildPropsPath for '{reference.ProjectPath}', so the controlled wrapper cannot be inherited safely.";
                return false;
            }
        }

        var isolatedPaths = new HashSet<string>(FileSystemPathSafety.ExistingPathComparer);
        var pendingImports = new Queue<(string Path, string ProjectDirectory,
            IReadOnlyDictionary<string, string> Properties, string? PackageRoot)>();
        var sourceContextsByImport = new Dictionary<string,
            (string Path, string ProjectDirectory,
                List<(IReadOnlyDictionary<string, string> Properties, string? PackageRoot)> Contexts)>(
                StringComparer.Ordinal);
        var sourceProperties = new Dictionary<string, List<IReadOnlyDictionary<string, string>>>(
            FileSystemPathSafety.ExistingPathComparer);
        var projectSources = new Dictionary<string, HashSet<string>>(
            FileSystemPathSafety.ExistingPathComparer);
        static bool SameProperties(IReadOnlyDictionary<string, string> first,
            IReadOnlyDictionary<string, string> second)
            => first.Count == second.Count && first.All(property => second.Any(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(property.Key, candidate.Key) &&
                StringComparer.Ordinal.Equals(property.Value, candidate.Value)));
        static string? ReadPackageRoot(IReadOnlyDictionary<string, string> properties)
            => properties.TryGetValue("NuGetPackageRoot", out string? root) ? root : null;
        void AddSource(string path, string projectDirectory,
            IReadOnlyDictionary<string, string> properties, string? packageRoot)
        {
            isolatedPaths.Add(path);
            if (!projectSources.TryGetValue(projectDirectory, out HashSet<string>? paths))
            {
                paths = new HashSet<string>(FileSystemPathSafety.ExistingPathComparer);
                projectSources[projectDirectory] = paths;
            }
            else
            {
                projectDirectory = projectSources.Keys.First(existing =>
                    FileSystemPathSafety.ExistingPathComparer.Equals(existing, projectDirectory));
            }
            paths.Add(path);
            if (!sourceProperties.TryGetValue(path, out List<IReadOnlyDictionary<string, string>>? contexts))
            {
                contexts = new List<IReadOnlyDictionary<string, string>>();
                sourceProperties[path] = contexts;
            }
            if (!contexts.Any(context => SameProperties(context, properties)))
                contexts.Add(properties);
            string key = Path.GetFullPath(path) + "\0" + projectDirectory;
            if (!sourceContextsByImport.TryGetValue(key, out var source))
            {
                source = (path, projectDirectory,
                    new List<(IReadOnlyDictionary<string, string> Properties, string? PackageRoot)>());
                sourceContextsByImport[key] = source;
            }
            if (!source.Contexts.Any(context => SameProperties(context.Properties, properties) &&
                    string.Equals(context.PackageRoot, packageRoot,
                        IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            {
                source.Contexts.Add((properties, packageRoot));
                pendingImports.Enqueue((path, projectDirectory, properties, packageRoot));
            }
        }
        if (isolatedProjects.Contains(Path.GetFullPath(rootRequest.ProjectPath)))
            AddSource(rootRequest.ProjectPath, Path.GetDirectoryName(rootRequest.ProjectPath)!,
                rootRequest.ReadEffectiveGlobalProperties(), ReadPackageRoot(rootEvaluatedProperties));
        foreach (string import in rootEvaluatedImports)
        {
            if (isolatedProjects.Contains(Path.GetFullPath(rootRequest.ProjectPath)) &&
                !IsControlledToolchainImport(import, rootEvaluatedProperties))
                AddSource(import, Path.GetDirectoryName(rootRequest.ProjectPath)!,
                    rootRequest.ReadEffectiveGlobalProperties(), ReadPackageRoot(rootEvaluatedProperties));
        }
        foreach (ControlledPublishGraphNode node in graphNodes)
        {
            bool isolated = isolatedProjects.Contains(Path.GetFullPath(node.Request.ProjectPath));
            if (isolated)
                AddSource(node.Request.ProjectPath, Path.GetDirectoryName(node.Request.ProjectPath)!,
                    node.Request.ReadEffectiveGlobalProperties(), ReadPackageRoot(node.EvaluatedProperties));
            foreach (string import in node.EvaluatedImports)
            {
                // The installed SDK supplies its own target-time output defaults. Custom
                // project and package imports can change the isolated context instead.
                if (IsControlledToolchainImport(import, node.EvaluatedProperties))
                    continue;
                if (isolated)
                    AddSource(import, Path.GetDirectoryName(node.Request.ProjectPath)!,
                        node.Request.ReadEffectiveGlobalProperties(), ReadPackageRoot(node.EvaluatedProperties));
            }
        }

        var documents = new Dictionary<string, XDocument>(FileSystemPathSafety.ExistingPathComparer);
        foreach (string path in isolatedPaths.Where(File.Exists))
        {
            try
            {
                documents[path] = XDocument.Load(path, LoadOptions.None);
            }
            catch
            {
                failureReason = $"controlled restore context source '{path}' could not be inspected.";
                return false;
            }
        }
        var unstableProperties = ReadControlledRestoreContextOverriddenPropertyNames();
        unstableProperties.UnionWith(ControlledInvocationGuardProperties);
        unstableProperties.UnionWith(ControlledContextPathProperties);
        Dictionary<string, string> ReadImmutableProperties(IReadOnlyDictionary<string, string> properties)
            => properties.Where(property => !unstableProperties.Contains(property.Key))
                .ToDictionary(property => property.Key, property => property.Value,
                    StringComparer.OrdinalIgnoreCase);
        bool discoveredContextImport;
        do
        {
            discoveredContextImport = false;
            while (pendingImports.Count > 0)
            {
                (string path, string projectDirectory, IReadOnlyDictionary<string, string> properties,
                    string? packageRoot) =
                    pendingImports.Dequeue();
                if (!File.Exists(path))
                    continue;
                if (!documents.TryGetValue(path, out XDocument? document))
                {
                    try
                    {
                        document = XDocument.Load(path, LoadOptions.None);
                        documents[path] = document;
                    }
                    catch
                    {
                        failureReason = $"controlled restore context source '{path}' could not be inspected.";
                        return false;
                    }
                }

                foreach (XElement import in document.Descendants().Where(element =>
                             element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
                {
                    bool contextDependent = import.AncestorsAndSelf()
                        .SelectMany(element => element.Attributes())
                        .Where(attribute => attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase))
                        .Any(attribute => ConditionDependsOnControlledContextValue(attribute.Value,
                            projectSources[projectDirectory].Where(documents.ContainsKey)
                                .Select(source => documents[source]), unstableProperties));
                    if (!contextDependent || IsDefinitelyInactiveControlledBuildOperation(
                            import, properties, definingProjectPath: null,
                            immutableGlobalProperties: ReadImmutableProperties(properties)))
                        continue;
                    if (!TryResolveControlledContextImport(path, projectDirectory,
                            import.Attribute("Project")?.Value, originalGitRoot,
                            verifiedPackageCatalogs, packageRoot,
                            out string? importedPath))
                    {
                        failureReason = $"context-dependent import '{import.Attribute("Project")?.Value}' in '{path}' cannot be inspected safely.";
                        return false;
                    }
                    if (File.Exists(importedPath))
                    {
                        if (IsSameOrBelowBuildInputPath(importedPath!, originalGitRoot) &&
                            HasReparsePointBelowRoot(importedPath!, originalGitRoot))
                        {
                            failureReason = $"context-dependent import '{importedPath}' traverses a link in the source checkout.";
                            return false;
                        }
                        discoveredContextImport |= !projectSources[projectDirectory].Contains(importedPath!);
                        AddSource(importedPath!, projectDirectory, properties, packageRoot);
                    }
                }
            }
            // A newly discovered props file may define an alias used by a later import
            // condition in a source document already scanned above.
            if (discoveredContextImport)
            {
                foreach (var source in sourceContextsByImport.Values)
                    foreach (var context in source.Contexts)
                        pendingImports.Enqueue((source.Path, source.ProjectDirectory,
                            context.Properties, context.PackageRoot));
            }
        }
        while (discoveredContextImport);

        if (!TryReadControlledToolchainTargetNames(rootEvaluatedImports, rootEvaluatedProperties,
                graphNodes, out HashSet<string> toolchainTargetNames))
        {
            failureReason = "controlled restore context toolchain targets could not be inspected.";
            return false;
        }

        // A later imported target can mutate an output path after the verifier has
        // already run. Fail before controlled execution for custom target writes.
        bool unknownLocalProperties = false;
        foreach (XDocument source in documents.Values)
        {
            string? localProperties = source.Root?.Attribute("TreatAsLocalProperty")?.Value;
            if (string.IsNullOrWhiteSpace(localProperties))
                continue;
            if (ContainsUnresolvedBuildExpression(localProperties!))
            {
                unknownLocalProperties = true;
                break;
            }
            unstableProperties.UnionWith(DecodeMsBuildEscapes(localProperties!).Split(';')
                .Select(name => name.Trim()).Where(name => name.Length > 0));
        }
        foreach (string path in isolatedPaths)
        {
            if (!documents.TryGetValue(path, out XDocument? document))
                continue;

            foreach (XElement target in document.Descendants().Where(element =>
                         element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourceProperties[path].All(properties =>
                    {
                        var immutable = unknownLocalProperties
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : ReadImmutableProperties(properties);
                        return IsDefinitelyInactiveControlledBuildOperation(target, properties,
                            definingProjectPath: null, immutableGlobalProperties: immutable);
                    }))
                    continue;
                if (IsDefinitelyUninvokedControlledContextTarget(target, documents,
                        toolchainTargetNames,
                        sourceProperties[path]))
                    continue;
                foreach (XElement assignment in target.Descendants())
                {
                    string? propertyName = assignment.Parent?.Name.LocalName.Equals(
                        "PropertyGroup", StringComparison.OrdinalIgnoreCase) == true
                        ? assignment.Name.LocalName
                        : assignment.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase)
                            ? assignment.Attributes().FirstOrDefault(attribute =>
                                attribute.Name.LocalName.Equals("PropertyName", StringComparison.OrdinalIgnoreCase))?.Value
                            : null;
                    if (propertyName is null || !ControlledContextPathProperties.Contains(propertyName))
                        continue;
                    if (sourceProperties[path].All(properties =>
                    {
                        var immutable = unknownLocalProperties
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : ReadImmutableProperties(properties);
                        return IsDefinitelyInactiveControlledBuildOperation(assignment, properties,
                            definingProjectPath: null, immutableGlobalProperties: immutable);
                    }))
                        continue;
                    failureReason = $"target-time assignment to an isolation-sensitive property '{propertyName}' in '{path}' cannot be proven safe for controlled restore contexts.";
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsControlledToolchainImport(
        string path,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        foreach (string name in new[] { "MSBuildSDKsPath", "MSBuildToolsPath" })
        {
            if (evaluatedProperties.TryGetValue(name, out string? root) &&
                !string.IsNullOrWhiteSpace(root) &&
                IsSameOrBelowBuildInputPath(path, root))
                return true;
        }
        return false;
    }

    private static bool ConditionDependsOnControlledContextValue(
        string? condition, IEnumerable<XDocument> documents,
        ISet<string> unstableProperties)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return false;
        var pending = new Queue<string>();
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(condition!);
        while (pending.Count > 0)
        {
            if (inspected.Count >= 128)
                return true;
            string expression = pending.Dequeue();
            if (!inspected.Add(expression))
                continue;
            foreach (Match match in Regex.Matches(expression,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)", RegexOptions.CultureInvariant))
            {
                string name = match.Groups[1].Value.Split('.')[0];
                if (unstableProperties.Contains(name))
                    return true;
                foreach (XElement property in documents.SelectMany(document => document.Descendants())
                             .Where(element => element.Parent?.Name.LocalName.Equals(
                                 "PropertyGroup", StringComparison.OrdinalIgnoreCase) == true &&
                             element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    pending.Enqueue(property.Value);
            }
        }
        return false;
    }

    private static bool TryResolveControlledContextImport(
        string declaringPath,
        string projectDirectory,
        string? projectExpression,
        string originalGitRoot,
        IReadOnlyCollection<VerifiedPackageInputCatalog> verifiedPackageCatalogs,
        string? packageRoot,
        out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(projectExpression))
            return false;
        try
        {
            string directory = Path.GetDirectoryName(declaringPath)!;
            string expression = ReplaceOrdinalIgnoreCase(
                DecodeMsBuildEscapes(projectExpression!),
                "$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar);
            expression = ReplaceOrdinalIgnoreCase(expression,
                "$(MSBuildProjectDirectory)", projectDirectory);
            if (expression.IndexOf("$(NuGetPackageRoot)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (string.IsNullOrWhiteSpace(packageRoot) || !Path.IsPathRooted(packageRoot))
                    return false;
                expression = ReplaceOrdinalIgnoreCase(expression,
                    "$(NuGetPackageRoot)", Path.GetFullPath(packageRoot).TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar);
            }
            if (ContainsUnresolvedBuildExpression(expression) ||
                expression.IndexOfAny(new[] { '*', '?' }) >= 0)
                return false;
            string resolvedPath = Path.GetFullPath(Path.IsPathRooted(expression)
                ? expression
                : Path.Combine(directory, expression));
            path = resolvedPath;
            return IsSameOrBelowBuildInputPath(resolvedPath, originalGitRoot) ||
                   verifiedPackageCatalogs.Any(catalog =>
                       catalog.TryVerify(resolvedPath, out bool isPackageInput) && isPackageInput);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDefinitelyUninvokedControlledContextTarget(
        XElement target,
        IReadOnlyDictionary<string, XDocument> documents,
        ISet<string> toolchainTargetNames,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> contexts)
    {
        string? name = target.Attribute("Name")?.Value;
        if (string.IsNullOrWhiteSpace(name) ||
            toolchainTargetNames.Contains(name!) ||
            new[] { "Restore", "Build", "GetTargetPath", "ComputeFilesToPublish" }
                .Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;
        string[] hooks = new[] { target.Attribute("BeforeTargets")?.Value,
                target.Attribute("AfterTargets")?.Value }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(';'))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (hooks.Any(hook =>
                !new[] { "Pack", "Clean", "Rebuild", "VSTest", "Test" }
                    .Contains(hook, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (hooks.Contains("Pack", StringComparer.OrdinalIgnoreCase) &&
            (contexts.Any(properties => properties.TryGetValue(
                 "GeneratePackageOnBuild", out string? value) &&
                 !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) ||
             documents.Values.SelectMany(document => document.Descendants()).Any(element =>
                 element.Name.LocalName.Equals("GeneratePackageOnBuild", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))))
            return false;
        if (contexts.Any(properties => properties.Values.Any(value =>
                value.Split(';').Any(candidate =>
                    candidate.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))) ||
            documents.Values.SelectMany(document => document.Descendants())
                .Where(element => element.Parent?.Name.LocalName.Equals(
                    "PropertyGroup", StringComparison.OrdinalIgnoreCase) == true)
                .Any(element => element.Value.Split(';').Any(candidate =>
                    candidate.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))))
            return false;

        foreach (XAttribute attribute in documents.Values
                     .SelectMany(document => document.Descendants().Attributes())
                     .Where(attribute => new[] { "DependsOnTargets", "Targets", "ExecuteTargets",
                         "InitialTargets", "DefaultTargets" }.Contains(
                         attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
        {
            string value = attribute.Value;
            if (ContainsUnresolvedBuildExpression(value) ||
                value.Split(';').Any(candidate =>
                    candidate.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static bool RemovesControlledWrapperPath(string value)
        => DecodeMsBuildEscapes(value).Split(';')
            .Any(name => name.Trim().Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase));
}
