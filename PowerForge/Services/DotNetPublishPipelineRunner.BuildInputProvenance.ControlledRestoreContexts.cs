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
        IReadOnlyDictionary<string, string?> controlledEnvironment,
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

        var controlledPropertyNames = ReadControlledRestoreContextOverriddenPropertyNames();

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
            string? defaultProps = null;
            while (directory is not null && IsSameOrBelowBuildInputPath(directory, controlledSourceRoot))
            {
                string candidate = Path.Combine(directory, "Directory.Build.props");
                if (File.Exists(candidate))
                {
                    defaultProps = candidate;
                    break;
                }
                if (FileSystemPathSafety.ExistingPathComparer.Equals(directory, controlledSourceRoot))
                    break;
                directory = Path.GetDirectoryName(directory);
            }
            string projectCondition = BuildControlledProjectPathCondition(controlledProjectPath);
            // The controlled wrapper replaces the directory-props entry point. Import the
            // project's original props first so its build settings retain their normal order.
            project.Add(new XElement("Import",
                new XAttribute("Project", "$(_PowerForgeOriginalDirectoryBuildPropsPath)"),
                new XAttribute("Condition", projectCondition +
                    " and '$(_PowerForgeOriginalDirectoryBuildPropsPath)' != ''")));
            if (defaultProps is not null)
                project.Add(new XElement("Import",
                    new XAttribute("Project", defaultProps),
                    new XAttribute("Condition", projectCondition +
                        " and '$(_PowerForgeOriginalDirectoryBuildPropsPath)' == ''")));

            ProjectEvaluationRequest[] representatives = group
                .GroupBy(BuildControlledRestoreContextKey, StringComparer.Ordinal)
                .Select(context => context.First())
                .ToArray();
            if (representatives.Length <= 1)
                continue;
            if ((controlledEnvironment.TryGetValue("ImportDirectoryBuildProps", out string? environmentImportProps) &&
                 !string.IsNullOrEmpty(environmentImportProps) &&
                 !string.Equals(environmentImportProps, "true", StringComparison.OrdinalIgnoreCase)) ||
                representatives.Any(request =>
                    request.ReadEffectiveGlobalProperties().TryGetValue(
                        "ImportDirectoryBuildProps", out string? importProps) &&
                    !string.Equals(importProps, "true", StringComparison.OrdinalIgnoreCase)))
            {
                failureReason = $"project '{group.Key}' disables Directory.Build.props imports, so its restore contexts cannot be isolated.";
                return false;
            }

            project.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", BuildControlledProjectPathCondition(controlledProjectPath)),
                new XElement("_PowerForgeRequiresContextIsolation", "true")));

            string[] propertyNames = representatives
                .SelectMany(request => request.ReadEffectiveGlobalProperties().Keys)
                .Where(name => !controlledPropertyNames.Contains(name))
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
                var condition = new StringBuilder(BuildControlledProjectPathCondition(controlledProjectPath));
                foreach (string name in propertyNames)
                {
                    string value = properties.TryGetValue(name, out string? presentValue)
                        ? presentValue
                        : string.Empty;
                    if (!TryRemapControlledBuildValue(
                            value,
                            originalGitRoot,
                            controlledSourceRoot,
                            Path.GetDirectoryName(request.ProjectPath)!,
                            out string controlledValue))
                    {
                        failureReason = $"project '{group.Key}' has a restore-context property that could not be remapped.";
                        return false;
                    }
                    string matchedName = name.Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase)
                        ? "_PowerForgeOriginalDirectoryBuildPropsPath"
                        : name;
                    condition.Append(" and '$(").Append(matchedName).Append(")' == '")
                        .Append(EscapeControlledMsBuildConditionLiteral(controlledValue)).Append("'");
                }
                if (!matchedConditions.Add(condition.ToString()))
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
                    new XAttribute("Condition", condition.ToString()),
                    new XElement("BaseIntermediateOutputPath", intermediate),
                    new XElement("MSBuildProjectExtensionsPath", intermediate),
                    new XElement("BaseOutputPath", output),
                    new XElement("_PowerForgeExpectedBaseIntermediateOutputPath", intermediate),
                    new XElement("_PowerForgeExpectedProjectExtensionsPath", intermediate),
                    new XElement("_PowerForgeExpectedBaseOutputPath", output),
                    // The SDK excludes BaseIntermediateOutputPath from default Compile items.
                    // Once that path is contextual, exclude the containing obj directory too,
                    // or another context's generated AssemblyInfo becomes a source input.
                    new XElement("DefaultItemExcludes",
                        "$(DefaultItemExcludes);" + Path.Combine(projectDirectory, "obj", "**")),
                    new XElement("_PowerForgeRestoreContextMatched", "true")));
            }
        }
        project.Add(new XElement("Target",
            new XAttribute("Name", "PowerForgeVerifyRestoreContext_" + Guid.NewGuid().ToString("N")),
            new XAttribute("BeforeTargets", "Restore;Build;GetTargetPath;ComputeFilesToPublish"),
            new XAttribute("Condition", "'$(_PowerForgeRequiresContextIsolation)' == 'true'"),
            new XElement("Error",
                new XAttribute("Condition", "'$(_PowerForgeRestoreContextMatched)' != 'true'"),
                new XAttribute("Text", "The controlled project restore context did not match the evaluated graph.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(BaseIntermediateOutputPath)' != '$(_PowerForgeExpectedBaseIntermediateOutputPath)'"),
                new XAttribute("Text", "The project overrides the controlled intermediate output path.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(MSBuildProjectExtensionsPath)' != '$(_PowerForgeExpectedProjectExtensionsPath)'"),
                new XAttribute("Text", "The project overrides the controlled restore assets path.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(BaseOutputPath)' != '$(_PowerForgeExpectedBaseOutputPath)'"),
                new XAttribute("Text", "The project overrides the controlled context output path.")),
            new XElement("PropertyGroup",
                new XElement("_PowerForgeActualIntermediateOutputPath",
                    new XAttribute("Condition", "'$(IntermediateOutputPath)' != ''"),
                    "$([System.IO.Path]::GetFullPath('$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(IntermediateOutputPath)'))'))"),
                new XElement("_PowerForgeActualProjectAssetsFile",
                    new XAttribute("Condition", "'$(ProjectAssetsFile)' != ''"),
                    "$([System.IO.Path]::GetFullPath('$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(ProjectAssetsFile)'))'))"),
                new XElement("_PowerForgeActualOutputPath",
                    "$([System.IO.Path]::GetFullPath('$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(OutputPath)'))'))"),
                new XElement("_PowerForgeActualOutDir",
                    new XAttribute("Condition", "'$(OutDir)' != ''"),
                    "$([System.IO.Path]::GetFullPath('$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(OutDir)'))'))"),
                new XElement("_PowerForgeActualTargetPath",
                    new XAttribute("Condition", "'$(TargetPath)' != ''"),
                    "$([System.IO.Path]::GetFullPath('$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(TargetPath)'))'))")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(IntermediateOutputPath)' != '' and $([System.String]::Copy('$(_PowerForgeActualIntermediateOutputPath)').StartsWith('$(_PowerForgeExpectedBaseIntermediateOutputPath)', System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project intermediate output path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(ProjectAssetsFile)' != '' and $([System.String]::Copy('$(_PowerForgeActualProjectAssetsFile)').StartsWith('$(_PowerForgeExpectedBaseIntermediateOutputPath)', System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project assets path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$([System.String]::Copy('$(_PowerForgeActualOutputPath)').StartsWith('$(_PowerForgeExpectedBaseOutputPath)', System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project output path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(OutDir)' != '' and $([System.String]::Copy('$(_PowerForgeActualOutDir)').StartsWith('$(_PowerForgeExpectedBaseOutputPath)', System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project OutDir is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "'$(TargetPath)' != '' and $([System.String]::Copy('$(_PowerForgeActualTargetPath)').StartsWith('$(_PowerForgeExpectedBaseOutputPath)', System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project TargetPath is outside its controlled context."))));
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

    private static HashSet<string> ReadControlledRestoreContextOverriddenPropertyNames()
    {
        var arguments = new List<string>();
        AppendControlledProofSafeguards(arguments, string.Empty, string.Empty, "unused.lock.json");
        AppendControlledRootGraphRestoreOverrides(arguments, "unused.lock.json");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TargetFramework",
            "TargetFrameworks",
            "RuntimeIdentifiers",
            "PathMap",
            "_TargetFrameworkOverride",
            "_DisableNuGetRestoreTargetFrameworksOverride"
        };
        foreach (string argument in arguments)
        {
            if (!argument.StartsWith("-p:", StringComparison.OrdinalIgnoreCase))
                continue;
            int separator = argument.IndexOf('=');
            if (separator > 3)
                names.Add(argument.Substring(3, separator - 3));
        }
        return names;
    }

    private static void AppendControlledRestoreContextProps(
        ICollection<string> arguments,
        string? propsPath)
    {
        if (propsPath is not null)
        {
            // Keep CustomAfterDirectoryBuildProps available to the SDK and the caller.
            const string originalPrefix = "-p:DirectoryBuildPropsPath=";
            string? original = arguments.FirstOrDefault(argument =>
                argument.StartsWith(originalPrefix, StringComparison.OrdinalIgnoreCase));
            if (original is not null)
            {
                arguments.Remove(original);
                arguments.Add("-p:_PowerForgeOriginalDirectoryBuildPropsPath=" +
                    original.Substring(originalPrefix.Length));
            }
            arguments.Add(originalPrefix + EscapeMsBuildPropertyValue(propsPath));
        }
    }
}
