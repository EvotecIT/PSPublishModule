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

    private static bool TryValidateControlledRestoreContextReferences(
        IReadOnlyCollection<ControlledPublishGraphNode> graphNodes,
        ProjectEvaluationRequest rootRequest,
        IReadOnlyCollection<EvaluatedProjectReference> rootProjectReferences,
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

        return true;
    }

    private static bool IsDefinitelyUninvokedControlledContextTarget(
        XElement target,
        IReadOnlyDictionary<string, XDocument> documents,
        ISet<string> toolchainTargetNames,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> contexts,
        IReadOnlyCollection<string> requestedTargets)
    {
        // Only the owner's known restore/build roots justify skipping Pack/Publish
        // hooks. Unknown or default roots cannot establish that either hook is unused.
        // Keep Clean/Rebuild hooks: the root proof requests Rebuild, which reaches Clean.
        if (requestedTargets.Count == 0 || requestedTargets.Any(name =>
                !new[] { "Restore", "Build", "Rebuild", "Clean", "GetTargetPath", "ComputeFilesToPublish" }
                    .Contains(name, StringComparer.OrdinalIgnoreCase)))
            return false;
        string? name = target.Attribute("Name")?.Value;
        if (string.IsNullOrWhiteSpace(name) ||
            toolchainTargetNames.Contains(name!) ||
            requestedTargets.Contains(name, StringComparer.OrdinalIgnoreCase) ||
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
                !new[] { "Pack", "Publish" }
                    .Contains(hook, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (hooks.Contains("Pack", StringComparer.OrdinalIgnoreCase) &&
            (contexts.Any(properties => properties.TryGetValue(
                 "GeneratePackageOnBuild", out string? value) &&
                 !string.IsNullOrWhiteSpace(value) &&
                 !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) ||
             documents.Values.SelectMany(document => document.Descendants()).Any(element =>
                 element.Name.LocalName.Equals("GeneratePackageOnBuild", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(element.Value) &&
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

        // A hook is also reachable when its destination (Pack/Publish) appears in
        // an evaluated dependency property or a literal target dependency.
        if (contexts.Any(properties => properties.Any(property =>
                IsControlledContextDependencyProperty(property.Key) &&
                (ContainsUnresolvedBuildExpression(property.Value) || property.Value.Split(';').Any(candidate => hooks.Contains(candidate.Trim(), StringComparer.OrdinalIgnoreCase))))) ||
            documents.Values.SelectMany(document => document.Descendants()).Any(IsControlledContextDependencyMutation))
            return false;

        foreach (XAttribute attribute in documents.Values
                     .SelectMany(document => document.Root?.DescendantsAndSelf().Attributes() ?? Enumerable.Empty<XAttribute>())
                     .Where(attribute => new[] { "DependsOnTargets", "Targets", "ExecuteTargets",
                         "InitialTargets", "DefaultTargets" }.Contains(
                         attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
        {
            string value = attribute.Value;
            if (ContainsUnresolvedBuildExpression(value) ||
                value.Split(';').Any(candidate =>
                    candidate.Trim().Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    hooks.Contains(candidate.Trim(), StringComparer.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static bool IsControlledContextDependencyProperty(string name)
        => name.EndsWith("DependsOn", StringComparison.OrdinalIgnoreCase) ||
           name.EndsWith("Targets", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlledContextDependencyMutation(XElement element)
    {
        if (!element.Ancestors().Any(parent => parent.Name.LocalName == "Target"))
            return false;
        string? name = element.Parent?.Name.LocalName == "PropertyGroup" ? element.Name.LocalName
            : element.Name.LocalName == "Output" ? element.Attribute("PropertyName")?.Value : null;
        if (name is null)
            return false;
        name = DecodeMsBuildEscapes(name);
        return ContainsUnresolvedBuildExpression(name) || IsControlledContextDependencyProperty(name);
    }

    private static bool RemovesControlledWrapperPath(string value)
        => DecodeMsBuildEscapes(value).Split(';')
            .Any(name => name.Trim().Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase));
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

}
