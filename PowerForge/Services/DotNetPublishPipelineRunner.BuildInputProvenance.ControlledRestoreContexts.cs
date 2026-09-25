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
        string nonce = Guid.NewGuid().ToString("N");
        string originalPropsProperty = BuildControlledOriginalPropsPropertyName(nonce);
        string originalFrameworksProperty = BuildControlledOriginalFrameworksPropertyName(nonce);
        string useOriginalFrameworksProperty = BuildControlledUseOriginalFrameworksPropertyName(nonce);
        string matchedProperty = "_PowerForgeMatched_" + nonce;
        string expectedIntermediateProperty = "_PowerForgeExpectedIntermediate_" + nonce;
        string expectedExtensionsProperty = "_PowerForgeExpectedExtensions_" + nonce;
        string expectedOutputProperty = "_PowerForgeExpectedOutput_" + nonce;
        string expectedObjExclusionProperty = "_PowerForgeExpectedObjExclusion_" + nonce;
        string expectedBinExclusionProperty = "_PowerForgeExpectedBinExclusion_" + nonce;
        string actualIntermediateProperty = "_PowerForgeActualIntermediate_" + nonce;
        string actualAssetsProperty = "_PowerForgeActualAssets_" + nonce;
        string actualOutputProperty = "_PowerForgeActualOutput_" + nonce;
        string actualOutDirProperty = "_PowerForgeActualOutDir_" + nonce;
        string actualTargetProperty = "_PowerForgeActualTarget_" + nonce;
        var isolatedProjectConditions = new List<string>();

        // The wrapper is supplied as a global property. Restore the original observable
        // value locally before importing it, including for code in the original props.
        var project = new XElement("Project",
            new XAttribute("TreatAsLocalProperty", "DirectoryBuildPropsPath"));
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
            if (controlledEnvironment.TryGetValue("DirectoryBuildPropsPath", out string? environmentProps) &&
                !string.IsNullOrWhiteSpace(environmentProps))
            {
                if (!Path.IsPathRooted(environmentProps))
                {
                    failureReason = $"project '{group.Key}' has a relative DirectoryBuildPropsPath that cannot be mapped into the controlled checkout.";
                    return false;
                }
                defaultProps = Path.GetFullPath(environmentProps);
                if (!IsSameOrBelowBuildInputPath(defaultProps, controlledSourceRoot) || !File.Exists(defaultProps))
                {
                    failureReason = $"project '{group.Key}' has a DirectoryBuildPropsPath outside the controlled checkout or missing from it.";
                    return false;
                }
            }
            while (defaultProps is null && directory is not null && IsSameOrBelowBuildInputPath(directory, controlledSourceRoot))
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
            ProjectEvaluationRequest[] representatives = group
                .GroupBy(BuildControlledRestoreContextKey, StringComparer.Ordinal)
                .Select(context => context.First())
                .ToArray();
            string[] propertyNames = representatives
                .SelectMany(request => request.ReadEffectiveGlobalProperties().Keys)
                .Where(name => !controlledPropertyNames.Contains(name) ||
                    name.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (propertyNames.Any(name => !IsValidControlledMsBuildPropertyName(name)))
            {
                failureReason = $"project '{group.Key}' has a restore-context property that cannot be matched safely.";
                return false;
            }
            var capturedProperties = propertyNames.Select((name, index) =>
                (Name: name, Snapshot: "_PowerForgeInput_" + nonce + "_" + index))
                .ToDictionary(entry => entry.Name, entry => entry.Snapshot, StringComparer.OrdinalIgnoreCase);
            // Capture the incoming values before the original props can supply defaults.
            project.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", projectCondition),
                propertyNames.Where(name => !name.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                    .Select(name => new XElement(capturedProperties[name],
                    "$(" + (name.Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase)
                        ? originalPropsProperty : name) + ")")),
                // Matrix restore supplies the declared framework list globally. Other builds,
                // including nested references, must match their actual incoming value.
                propertyNames.Where(name => name.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(name => new[]
                    {
                        new XElement(capturedProperties[name],
                            new XAttribute("Condition", "'$(" + useOriginalFrameworksProperty + ")' == 'true'"),
                            "$(" + originalFrameworksProperty + ")"),
                        new XElement(capturedProperties[name],
                            new XAttribute("Condition", "'$(" + useOriginalFrameworksProperty + ")' != 'true'"),
                            "$(TargetFrameworks)")
                    })));
            project.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", projectCondition),
                new XElement("DirectoryBuildPropsPath",
                    new XAttribute("Condition", "$(" + originalPropsProperty + ".Length) != 0"),
                    "$(" + originalPropsProperty + ")"),
                new XElement("DirectoryBuildPropsPath",
                    new XAttribute("Condition", "$(" + originalPropsProperty + ".Length) == 0"),
                    defaultProps ?? string.Empty)));
            // The controlled wrapper replaces the directory-props entry point. Import the
            // project's original props first so its build settings retain their normal order.
            // The SDK skips a nonexistent DirectoryBuildPropsPath rather than failing the build.
            project.Add(new XElement("Import",
                new XAttribute("Project", "$(" + originalPropsProperty + ")"),
                new XAttribute("Condition", projectCondition +
                    " and Exists($(" + originalPropsProperty + "))")));
            if (defaultProps is not null)
                project.Add(new XElement("Import",
                    new XAttribute("Project", defaultProps),
                    new XAttribute("Condition", projectCondition +
                        " and $(" + originalPropsProperty + ".Length) == 0")));

            if (representatives.Length <= 1)
                continue;
            isolatedProjectConditions.Add(projectCondition);
            if ((controlledEnvironment.TryGetValue("ImportDirectoryBuildProps", out string? environmentImportProps) &&
                 !string.IsNullOrEmpty(environmentImportProps) &&
                 !string.Equals(environmentImportProps, "true", StringComparison.OrdinalIgnoreCase)) ||
                representatives.Any(request =>
                    request.ReadEffectiveGlobalProperties().TryGetValue(
                        "ImportDirectoryBuildProps", out string? importProps) &&
                    !string.Equals(importProps, "true", StringComparison.OrdinalIgnoreCase)) ||
                graphNodes.Any(node =>
                    FileSystemPathSafety.ExistingPathComparer.Equals(
                        Path.GetFullPath(node.Request.ProjectPath), group.Key) &&
                    node.EvaluatedProperties.TryGetValue(
                        "ImportDirectoryBuildProps", out string? evaluatedImportProps) &&
                    !string.IsNullOrEmpty(evaluatedImportProps) &&
                    !string.Equals(evaluatedImportProps, "true", StringComparison.OrdinalIgnoreCase)))
            {
                failureReason = $"project '{group.Key}' disables Directory.Build.props imports, so its restore contexts cannot be isolated.";
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
                    bool suppliedByGraph = properties.TryGetValue(name, out string? presentValue);
                    string value = suppliedByGraph
                        ? presentValue!
                        : !name.Equals("DirectoryBuildPropsPath", StringComparison.OrdinalIgnoreCase) &&
                          controlledEnvironment.TryGetValue(name, out string? environmentValue)
                            ? environmentValue ?? string.Empty
                            : string.Empty;
                    if (suppliedByGraph && !TryRemapControlledBuildValue(
                            value,
                            originalGitRoot,
                            controlledSourceRoot,
                            Path.GetDirectoryName(request.ProjectPath)!,
                            out value))
                    {
                        failureReason = $"project '{group.Key}' has a restore-context property that could not be remapped.";
                        return false;
                    }
                    string matchedName = capturedProperties[name];
                    condition.Append(" and $(").Append(matchedName).Append(".Equals('")
                        .Append(EscapeControlledMsBuildConditionLiteral(value))
                        .Append("', System.StringComparison.OrdinalIgnoreCase))");
                }
                if (!matchedConditions.Add(condition.ToString()))
                {
                    failureReason = $"project '{group.Key}' has restore contexts that MSBuild cannot distinguish.";
                    return false;
                }
                string suffix = ComputeControlledRestoreContextDirectoryName(controlledProjectPath, contextKey);
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
                    new XElement(expectedIntermediateProperty, intermediate),
                    new XElement(expectedExtensionsProperty, intermediate),
                    new XElement(expectedOutputProperty, output),
                    new XElement(expectedObjExclusionProperty,
                        Path.Combine(projectDirectory, "obj", "**")),
                    new XElement(expectedBinExclusionProperty,
                        Path.Combine(projectDirectory, "bin", "**")),
                    // The SDK excludes BaseIntermediateOutputPath from default Compile items.
                    // Once those paths are contextual, exclude both whole output trees so
                    // another context's generated files cannot become default items.
                    new XElement("DefaultItemExcludes",
                        "$(DefaultItemExcludes);" + Path.Combine(projectDirectory, "obj", "**") +
                        ";" + Path.Combine(projectDirectory, "bin", "**")),
                    new XElement(matchedProperty, "true")));
            }
        }
        // Property instance methods keep apostrophes in paths out of MSBuild's quoted
        // condition and property-function argument syntax.
        project.Add(new XElement("Target",
            new XAttribute("Name", BuildControlledRestoreContextVerifierTargetName(nonce)),
            new XAttribute("BeforeTargets", "Restore;Build;GetTargetPath;ComputeFilesToPublish"),
            new XAttribute("Condition", string.Join(" Or ", isolatedProjectConditions.Select(condition => "(" + condition + ")"))),
            new XElement("Error",
                new XAttribute("Condition", "'$(" + matchedProperty + ")' != 'true'"),
                new XAttribute("Text", "The controlled project restore context did not match the evaluated graph.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(BaseIntermediateOutputPath.Equals($(" + expectedIntermediateProperty + "))) != 'True'"),
                new XAttribute("Text", "The project overrides the controlled intermediate output path.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(MSBuildProjectExtensionsPath.Equals($(" + expectedExtensionsProperty + "))) != 'True'"),
                new XAttribute("Text", "The project overrides the controlled restore assets path.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(BaseOutputPath.Equals($(" + expectedOutputProperty + "))) != 'True'"),
                new XAttribute("Text", "The project overrides the controlled context output path.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(DefaultItemExcludes.Contains($(" + expectedObjExclusionProperty + "))) != 'True' or " +
                    "$(DefaultItemExcludes.Contains($(" + expectedBinExclusionProperty + "))) != 'True'"),
                new XAttribute("Text", "The project removes controlled output-tree exclusions.")),
            new XElement("PropertyGroup",
                new XElement(actualIntermediateProperty,
                    new XAttribute("Condition", "$(IntermediateOutputPath.Length) != 0"),
                    "$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine($(MSBuildProjectDirectory), $(IntermediateOutputPath)))))"),
                new XElement(actualAssetsProperty,
                    new XAttribute("Condition", "$(ProjectAssetsFile.Length) != 0"),
                    "$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine($(MSBuildProjectDirectory), $(ProjectAssetsFile)))))"),
                new XElement(actualOutputProperty,
                    "$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine($(MSBuildProjectDirectory), $(OutputPath)))))"),
                new XElement(actualOutDirProperty,
                    new XAttribute("Condition", "$(OutDir.Length) != 0"),
                    "$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine($(MSBuildProjectDirectory), $(OutDir)))))"),
                new XElement(actualTargetProperty,
                    new XAttribute("Condition", "$(TargetPath.Length) != 0"),
                    "$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine($(MSBuildProjectDirectory), $(TargetPath)))))")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(IntermediateOutputPath.Length) != 0 and $(" + actualIntermediateProperty +
                    ".StartsWith($(" + expectedIntermediateProperty + "), System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project intermediate output path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(ProjectAssetsFile.Length) != 0 and $(" + actualAssetsProperty +
                    ".StartsWith($(" + expectedIntermediateProperty + "), System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project assets path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(" + actualOutputProperty + ".StartsWith($(" + expectedOutputProperty +
                    "), System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project output path is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(OutDir.Length) != 0 and $(" + actualOutDirProperty +
                    ".StartsWith($(" + expectedOutputProperty + "), System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project OutDir is outside its controlled context.")),
            new XElement("Error",
                new XAttribute("Condition",
                    "$(TargetPath.Length) != 0 and $(" + actualTargetProperty +
                    ".StartsWith($(" + expectedOutputProperty + "), System.StringComparison.Ordinal)) != 'True'"),
                new XAttribute("Text", "The project TargetPath is outside its controlled context."))));
        propsPath = Path.Combine(controlledOutputRoot, "PowerForge.ControlledRestoreContexts." + nonce + ".props");
        new XDocument(project).Save(propsPath);
        return true;
    }

    private static string BuildControlledProjectPathCondition(string path)
        => "$(MSBuildProjectFullPath.Equals('" + EscapeControlledMsBuildConditionLiteral(path) +
           "', System.StringComparison." + (IsWindows() ? "OrdinalIgnoreCase" : "Ordinal") + "))";

    internal static string ComputeControlledRestoreContextDirectoryName(string controlledProjectPath, string contextKey)
        => ComputeSha256Hex(Encoding.UTF8.GetBytes(
            Path.GetFullPath(controlledProjectPath) + "\0" + contextKey)).Substring(0, 24);

    private static string BuildControlledOriginalPropsPropertyName(string nonce)
        => "_PowerForgeOriginalDirectoryBuildPropsPath_" + nonce;

    private static string BuildControlledRestoreContextVerifierTargetName(string nonce)
        => "PowerForgeVerifyRestoreContext_" + nonce;

    private static string BuildControlledRestoreContextVerifierTargetNameFromProps(string propsPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(propsPath);
        return BuildControlledRestoreContextVerifierTargetName(
            fileName.Substring(fileName.LastIndexOf('.') + 1));
    }

    private static string BuildControlledOriginalFrameworksPropertyName(string nonce)
        => "_PowerForgeOriginalTargetFrameworks_" + nonce;

    private static string BuildControlledUseOriginalFrameworksPropertyName(string nonce)
        => "_PowerForgeUseOriginalTargetFrameworks_" + nonce;

    private static bool TryAppendControlledOriginalFrameworksContext(
        ICollection<string> arguments,
        ProjectEvaluationRequest request,
        string? propsPath,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment)
    {
        if (propsPath is null)
            return true;
        string fileName = Path.GetFileNameWithoutExtension(propsPath);
        string nonce = fileName.Substring(fileName.LastIndexOf('.') + 1);
        request.ReadEffectiveGlobalProperties().TryGetValue("TargetFrameworks", out string? original);
        if (original is null)
            controlledEnvironment.TryGetValue("TargetFrameworks", out original);
        if (!TryRemapControlledBuildValue(original ?? string.Empty, originalGitRoot,
                controlledSourceRoot, Path.GetDirectoryName(request.ProjectPath)!,
                out string controlledOriginal))
            return false;
        arguments.Add("-p:" + BuildControlledOriginalFrameworksPropertyName(nonce) +
            "=" + EscapeMsBuildPropertyValue(controlledOriginal));
        arguments.Add("-p:" + BuildControlledUseOriginalFrameworksPropertyName(nonce) + "=true");
        return true;
    }

    private static bool TryPrependControlledContextIntermediatePathMap(
        ControlledPublishGraphNode node,
        string originalGitRoot,
        string controlledProjectPath,
        ref string pathMap)
    {
        if (!node.EvaluatedProperties.TryGetValue("BaseIntermediateOutputPath", out string? basePath) ||
            string.IsNullOrWhiteSpace(basePath))
            return false;
        string originalProjectDirectory = Path.GetDirectoryName(node.Request.ProjectPath)!;
        string originalIntermediate = NormalizeBuildInputPathRoot(Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(originalProjectDirectory, basePath));
        if (!IsSameOrBelowBuildInputPath(originalIntermediate, originalGitRoot))
            return false;
        string originalMappedIntermediate = originalIntermediate;
        string? bestSource = null;
        if (!string.IsNullOrWhiteSpace(node.PathMap))
        {
            foreach (string entry in node.PathMap!.Split(','))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0)
                    return false;
                string source = NormalizeBuildInputPathRoot(entry.Substring(0, separator).Trim());
                string target = entry.Substring(separator + 1).Trim();
                if (target.Length == 0 || !IsSameOrBelowBuildInputPath(originalIntermediate, source) ||
                    (bestSource is not null && source.Length <= bestSource.Length))
                    continue;
                string relative = FrameworkCompatibility.GetRelativePath(source, originalIntermediate);
                originalMappedIntermediate = relative == "."
                    ? target
                    : target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                      Path.DirectorySeparatorChar + relative;
                bestSource = source;
            }
        }
        string controlledProjectDirectory = Path.GetDirectoryName(controlledProjectPath)!;
        string suffix = ComputeControlledRestoreContextDirectoryName(controlledProjectPath,
            BuildControlledRestoreContextKey(node));
        string controlledIntermediate = NormalizeBuildInputPathRoot(Path.Combine(
            controlledProjectDirectory, "obj", "powerforge-context", suffix));
        // Roslyn embeds generated source paths in portable symbols. Hide only the
        // context-specific segment, preserving any caller-supplied original PathMap.
        pathMap = controlledIntermediate + "=" + originalMappedIntermediate + "," + pathMap;
        return true;
    }

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
            string fileName = Path.GetFileNameWithoutExtension(propsPath);
            string nonce = fileName.Substring(fileName.LastIndexOf('.') + 1);
            string originalPropsProperty = BuildControlledOriginalPropsPropertyName(nonce);
            string? original = arguments.FirstOrDefault(argument =>
                argument.StartsWith(originalPrefix, StringComparison.OrdinalIgnoreCase));
            if (original is not null)
                arguments.Remove(original);
            // A global assignment, including the empty case, prevents earlier imports or
            // controlled environment variables from redirecting this private import hook.
            arguments.Add("-p:" + originalPropsProperty + "=" +
                (original is null ? string.Empty : original.Substring(originalPrefix.Length)));
            arguments.Add(originalPrefix + EscapeMsBuildPropertyValue(propsPath));
        }
    }
}
