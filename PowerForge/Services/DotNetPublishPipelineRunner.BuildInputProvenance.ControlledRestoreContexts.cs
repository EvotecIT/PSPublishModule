using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryCreateControlledRestoreContextProps(
        ProjectEvaluationRequest rootRequest,
        IReadOnlyCollection<ControlledPublishGraphNode> graphNodes,
        string originalGitRoot,
        string controlledSourceRoot,
        string controlledOutputRoot,
        out string? propsPath,
        out string? failureReason)
    {
        propsPath = null;
        failureReason = null;
        var contexts = graphNodes.Select(node => node.Request)
            .Append(rootRequest)
            .GroupBy(request => Path.GetFullPath(request.ProjectPath),
                FileSystemPathSafety.ExistingPathComparer)
            .ToArray();
        if (!contexts.Any(group => group.Select(BuildControlledRestoreContextKey)
                .Distinct(StringComparer.Ordinal).Skip(1).Any()))
            return true;

        var project = new XElement("Project");
        foreach (IGrouping<string, ProjectEvaluationRequest> group in contexts)
        {
            string controlledProjectPath = Path.GetFullPath(Path.Combine(
                controlledSourceRoot,
                FrameworkCompatibility.GetRelativePath(originalGitRoot, group.Key)));
            if (!IsSameOrBelowBuildInputPath(controlledProjectPath, controlledSourceRoot))
            {
                failureReason = $"project '{group.Key}' could not be mapped to the controlled checkout.";
                return false;
            }

            string? directory = Path.GetDirectoryName(controlledProjectPath);
            string? originalProps = null;
            while (directory is not null && IsSameOrBelowBuildInputPath(directory, controlledSourceRoot))
            {
                string candidate = Path.Combine(directory, "Directory.Build.props");
                if (File.Exists(candidate))
                {
                    originalProps = candidate;
                    break;
                }
                if (FileSystemPathSafety.ExistingPathComparer.Equals(directory, controlledSourceRoot))
                    break;
                directory = Path.GetDirectoryName(directory);
            }
            if (originalProps is not null)
                project.Add(new XElement("Import",
                    new XAttribute("Project", originalProps),
                    new XAttribute("Condition", BuildControlledProjectPathCondition(controlledProjectPath))));

            ProjectEvaluationRequest[] representatives = group
                .GroupBy(BuildControlledRestoreContextKey, StringComparer.Ordinal)
                .Select(context => context.First())
                .ToArray();
            if (representatives.Length <= 1)
                continue;

            project.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", BuildControlledProjectPathCondition(controlledProjectPath)),
                new XElement("_PowerForgeRequiresContextIsolation", "true")));

            string[] propertyNames = representatives
                .SelectMany(request => request.ReadEffectiveGlobalProperties().Keys)
                .Where(name => !name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (propertyNames.Any(name => !IsValidControlledMsBuildPropertyName(name)))
            {
                failureReason = $"project '{group.Key}' has a restore-context property that cannot be matched safely.";
                return false;
            }
            var matchedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contextDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectEvaluationRequest request in representatives)
            {
                IReadOnlyDictionary<string, string> properties = request.ReadEffectiveGlobalProperties();
                string contextKey = BuildControlledRestoreContextKey(request);
                string condition = BuildControlledProjectPathCondition(controlledProjectPath) +
                    string.Concat(propertyNames.Select(name =>
                        " and '$(" + name + ")' == '" +
                        EscapeControlledMsBuildConditionLiteral(properties.TryGetValue(name, out string? value)
                            ? value : string.Empty) + "'"));
                if (!matchedConditions.Add(condition))
                {
                    failureReason = $"project '{group.Key}' has restore contexts that MSBuild cannot distinguish.";
                    return false;
                }
                string suffix = ComputeSha256Hex(Encoding.UTF8.GetBytes(contextKey)).Substring(0, 16);
                if (!contextDirectories.Add(suffix))
                {
                    failureReason = $"project '{group.Key}' has restore contexts with a conflicting directory identity.";
                    return false;
                }
                string projectDirectory = Path.GetDirectoryName(controlledProjectPath)!;
                string intermediate = Path.Combine(projectDirectory, "obj", "powerforge-context", suffix) +
                    Path.DirectorySeparatorChar;
                string output = Path.Combine(projectDirectory, "bin", "powerforge-context", suffix) +
                    Path.DirectorySeparatorChar;
                project.Add(new XElement("PropertyGroup",
                    new XAttribute("Condition", condition),
                    new XElement("BaseIntermediateOutputPath", intermediate),
                    new XElement("MSBuildProjectExtensionsPath", intermediate),
                    new XElement("BaseOutputPath", output),
                    // The SDK excludes BaseIntermediateOutputPath from default Compile items.
                    // Once that path is contextual, exclude the containing obj directory too,
                    // or another context's generated AssemblyInfo becomes a source input.
                    new XElement("DefaultItemExcludes",
                        "$(DefaultItemExcludes);" + Path.Combine(projectDirectory, "obj", "**")),
                    new XElement("_PowerForgeRestoreContextMatched", "true")));
            }
        }
        project.Add(new XElement("Target",
            new XAttribute("Name", "PowerForgeVerifyRestoreContext"),
            new XAttribute("BeforeTargets", "Restore;Build;GetTargetPath;ComputeFilesToPublish"),
            new XAttribute("Condition", "'$(_PowerForgeRequiresContextIsolation)' == 'true'"),
            new XElement("Error",
                new XAttribute("Condition", "'$(_PowerForgeRestoreContextMatched)' != 'true'"),
                new XAttribute("Text", "The controlled project restore context did not match the evaluated graph."))));
        propsPath = Path.Combine(controlledOutputRoot, "PowerForge.ControlledRestoreContexts.props");
        new XDocument(project).Save(propsPath);
        return true;
    }

    private static string BuildControlledProjectPathCondition(string path)
        => "'$(MSBuildProjectFullPath)' == '" + EscapeControlledMsBuildConditionLiteral(path) + "'";

    private static string EscapeControlledMsBuildConditionLiteral(string value)
        => EscapeMsBuildPropertyValue(value).Replace("'", "%27");

    private static bool IsValidControlledMsBuildPropertyName(string name)
    {
        try
        {
            _ = System.Xml.XmlConvert.VerifyName(name);
            return !name.Contains(':');
        }
        catch
        {
            return false;
        }
    }

    private static void AppendControlledRestoreContextProps(
        ICollection<string> arguments,
        string? propsPath)
    {
        if (propsPath is not null)
            arguments.Add("-p:DirectoryBuildPropsPath=" + EscapeMsBuildPropertyValue(propsPath));
    }
}
