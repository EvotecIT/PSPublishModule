namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string[] SelectControlledMultiFrameworkRestoreFrameworks(
        IReadOnlyDictionary<string, string> evaluatedProperties,
        IEnumerable<string?> selectedFrameworks)
    {
        string[] declared = ReadControlledDeclaredTargetFrameworks(evaluatedProperties);
        if (declared.Length <= 1)
            return Array.Empty<string>();

        string[] selected = selectedFrameworks
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Select(framework => framework!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length <= 1 || selected.Any(framework =>
                !declared.Contains(framework, StringComparer.OrdinalIgnoreCase)))
            return Array.Empty<string>();

        return selected;
    }

    private static string[] ReadControlledDeclaredTargetFrameworks(
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        string? declaredFrameworks = ReadControlledDeclaredTargetFrameworksValue(evaluatedProperties);
        if (declaredFrameworks is null)
            return Array.Empty<string>();

        return declaredFrameworks
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(framework => framework.Trim())
            .Where(framework => framework.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadControlledDeclaredTargetFrameworksValue(
        IReadOnlyDictionary<string, string> evaluatedProperties)
        => evaluatedProperties.TryGetValue("TargetFrameworks", out string? declaredFrameworks) &&
           !string.IsNullOrWhiteSpace(declaredFrameworks)
            ? declaredFrameworks
            : null;

    private static bool TryRestoreControlledMultiFrameworkProject(
        string originalProjectPath,
        ControlledPublishGraphNode representative,
        IReadOnlyCollection<string> frameworks,
        string declaredFrameworks,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot,
        string? restoreContextProps,
        out string? failureReason)
    {
        failureReason = null;
        string controlledProjectPath = Path.GetFullPath(Path.Combine(
            controlledSourceRoot,
            FrameworkCompatibility.GetRelativePath(originalGitRoot, originalProjectPath)));
        if (!IsSameOrBelowBuildInputPath(controlledProjectPath, controlledSourceRoot) ||
            !File.Exists(controlledProjectPath))
        {
            failureReason = $"controlled project '{originalProjectPath}' is missing or outside the controlled checkout.";
            return false;
        }

        var arguments = new List<string>
        {
            "msbuild",
            controlledProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-target:Restore"
        };
        if (!TryAppendControlledProjectEvaluationProperties(
                arguments,
                representative.Request,
                originalGitRoot,
                controlledSourceRoot))
        {
            failureReason = $"properties for controlled project '{originalProjectPath}' could not be remapped.";
            return false;
        }
        arguments.RemoveAll(argument =>
            argument.StartsWith("-p:TargetFramework=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-p:TargetFrameworks=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-p:_TargetFrameworkOverride=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-p:_DisableNuGetRestoreTargetFrameworksOverride=", StringComparison.OrdinalIgnoreCase));
        // NuGet's restore targets support a restore-only framework override. Preserve the
        // project's declared TargetFrameworks value so project conditions retain their exact
        // semantics while the assets file is limited to frameworks selected by this build graph.
        // The behavioral provenance tests intentionally guard this SDK integration point.
        arguments.Add("-p:TargetFrameworks=" + EscapeMsBuildPropertyValue(declaredFrameworks));
        arguments.Add("-p:_TargetFrameworkOverride=" + BuildMsBuildListPropertyValue(frameworks));
        arguments.Add("-p:_DisableNuGetRestoreTargetFrameworksOverride=true");
        arguments.Add("-p:BuildProjectReferences=false");
        arguments.Add("-p:RestoreRecursive=false");
        AppendControlledRestoreContextProps(arguments, restoreContextProps);
        if (!TryAppendControlledOriginalFrameworksContext(arguments, representative.Request,
                restoreContextProps, originalGitRoot, controlledSourceRoot, controlledEnvironment))
        {
            failureReason = $"project '{originalProjectPath}' TargetFrameworks could not be remapped.";
            return false;
        }
        AppendControlledProofSafeguards(
            arguments,
            controlledNuGetConfig,
            offlinePackageSourceList,
            Path.Combine(controlledOutputRoot, "multi-framework-packages.lock.json"));

        var process = RunControlledMsBuildEvaluationProcess(
            Path.GetDirectoryName(controlledProjectPath)!,
            arguments,
            controlledEnvironment,
            TimeSpan.FromMinutes(5),
            controlledOutputRoot);
        if (process.ExitCode == 0 && !process.TimedOut)
            return true;

        failureReason = process.TimedOut
            ? $"project '{originalProjectPath}' multi-framework restore timed out."
            : $"project '{originalProjectPath}' multi-framework restore exited with code {process.ExitCode}." +
              ReadControlledProcessFailureDetail(process);
        return false;
    }

    private static bool HasDistinctControlledProjectRestoreContexts(
        IReadOnlyCollection<ControlledPublishGraphNode> nodes)
    {
        return nodes
            .GroupBy(
                node => Path.GetFullPath(node.Request.ProjectPath),
                FileSystemPathSafety.ExistingPathComparer)
            .Any(group => group
                .Select(BuildControlledRestoreContextKey)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count() > 1);
    }

    private static string BuildControlledRestoreContextKey(ControlledPublishGraphNode node)
        => BuildControlledRestoreContextKey(node.Request);

    private static string BuildControlledRestoreContextKey(ProjectEvaluationRequest request)
    {
        var key = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, string> property in request.ReadEffectiveGlobalProperties()
                     .Where(property =>
                         !property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
        {
            AppendProjectReferenceKeySegment(key, property.Key.ToUpperInvariant());
            AppendProjectReferenceKeySegment(key, property.Value);
        }
        return key.ToString();
    }

    private static bool TryBuildControlledPublishGraphNode(
        ControlledPublishGraphNode node,
        bool restore,
        bool isolatedRestoreContext,
        string canonicalControlledProjectPath,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot,
        string? restoreContextProps,
        out string? failureReason)
    {
        failureReason = null;
        string originalProjectPath = Path.GetFullPath(node.Request.ProjectPath);
        if (!IsSameOrBelowBuildInputPath(originalProjectPath, originalGitRoot))
        {
            failureReason = $"project '{originalProjectPath}' is outside the controlled Git root.";
            return false;
        }
        string controlledProjectPath = Path.GetFullPath(Path.Combine(
            controlledSourceRoot,
            FrameworkCompatibility.GetRelativePath(originalGitRoot, originalProjectPath)));
        if (!IsSameOrBelowBuildInputPath(controlledProjectPath, controlledSourceRoot) ||
            !File.Exists(controlledProjectPath))
        {
            failureReason = $"controlled project '{originalProjectPath}' is missing or outside the controlled checkout.";
            return false;
        }

        var arguments = new List<string>
        {
            "msbuild",
            controlledProjectPath,
            "-nologo",
            "-maxCpuCount:1",
            "-nodeReuse:false",
            "-verbosity:quiet",
            "-target:Build" + (isolatedRestoreContext && restoreContextProps is not null
                ? ";" + BuildControlledRestoreContextVerifierTargetNameFromProps(restoreContextProps)
                : string.Empty)
        };
        if (restore)
            arguments.Add("-restore");
        if (!TryAppendControlledProjectEvaluationProperties(
                arguments,
                node.Request,
                originalGitRoot,
                controlledSourceRoot))
        {
            failureReason = $"properties for controlled project '{originalProjectPath}' could not be remapped.";
            return false;
        }
        arguments.Add("-p:BuildProjectReferences=false");
        arguments.Add("-p:RestoreRecursive=false");
        AppendControlledRestoreContextProps(arguments, restoreContextProps);
        if (!TryBuildControlledPathMap(
                controlledSourceRoot,
                originalGitRoot,
                node.PathMap,
                out string controlledPathMap))
        {
            failureReason = $"PathMap for controlled project '{originalProjectPath}' could not be constructed.";
            return false;
        }
        if (restoreContextProps is not null && isolatedRestoreContext &&
            !TryPrependControlledContextIntermediatePathMap(node,
                canonicalControlledProjectPath, ref controlledPathMap))
        {
            failureReason = $"the original intermediate path for project '{originalProjectPath}' could not be mapped.";
            return false;
        }
        arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
        AppendControlledProofSafeguards(
            arguments,
            controlledNuGetConfig,
            offlinePackageSourceList,
            Path.Combine(controlledOutputRoot, "packages.lock.json"));

        var process = RunControlledMsBuildEvaluationProcess(
            Path.GetDirectoryName(controlledProjectPath)!,
            arguments,
            controlledEnvironment,
            TimeSpan.FromMinutes(5),
            controlledOutputRoot);
        if (process.ExitCode == 0 && !process.TimedOut)
            return true;

        string? detail = TailLines(
            string.IsNullOrWhiteSpace(process.StdErr) ? process.StdOut : process.StdErr,
            maxLines: 8,
            maxChars: 2000);
        failureReason = process.TimedOut
            ? $"project '{originalProjectPath}' timed out."
            : $"project '{originalProjectPath}' exited with code {process.ExitCode}.";
        if (!string.IsNullOrWhiteSpace(detail))
            failureReason += " " + detail!.Trim();
        return false;
    }
}
