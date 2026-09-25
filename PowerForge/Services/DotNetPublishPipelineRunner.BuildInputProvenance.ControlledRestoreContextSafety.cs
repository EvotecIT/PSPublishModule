using System.Xml.Linq;

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
        IReadOnlyCollection<EvaluatedProjectReference> rootProjectReferences,
        IReadOnlyCollection<string> rootEvaluatedImports,
        IReadOnlyDictionary<string, string> rootEvaluatedProperties,
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
        if (isolatedProjects.Contains(Path.GetFullPath(rootRequest.ProjectPath)))
            isolatedPaths.Add(rootRequest.ProjectPath);
        foreach (string import in rootEvaluatedImports)
        {
            if (isolatedProjects.Contains(Path.GetFullPath(rootRequest.ProjectPath)) &&
                !IsControlledToolchainImport(import, rootEvaluatedProperties))
                isolatedPaths.Add(import);
        }
        foreach (ControlledPublishGraphNode node in graphNodes)
        {
            bool isolated = isolatedProjects.Contains(Path.GetFullPath(node.Request.ProjectPath));
            if (isolated)
                isolatedPaths.Add(node.Request.ProjectPath);
            foreach (string import in node.EvaluatedImports)
            {
                // The installed SDK supplies its own target-time output defaults. Custom
                // project and package imports can change the isolated context instead.
                if (IsControlledToolchainImport(import, node.EvaluatedProperties))
                    continue;
                if (isolated)
                    isolatedPaths.Add(import);
            }
        }

        // A later imported target can mutate an output path after the verifier has
        // already run. Fail before controlled execution for custom target writes.
        foreach (string path in isolatedPaths)
        {
            if (!File.Exists(path))
                continue;
            XDocument document;
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
            }
            catch
            {
                failureReason = $"controlled restore context source '{path}' could not be inspected.";
                return false;
            }

            foreach (XElement target in document.Descendants().Where(element =>
                         element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)))
            {
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

    private static bool RemovesControlledWrapperPath(string value)
        => DecodeMsBuildEscapes(value).Split(';')
            .Any(name => name.Trim().Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase));
}
