namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string[] SelectControlledMultiFrameworkRestoreFrameworks(
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        if (!evaluatedProperties.TryGetValue("TargetFrameworks", out string? declaredFrameworks) ||
            string.IsNullOrWhiteSpace(declaredFrameworks))
        {
            return Array.Empty<string>();
        }

        string[] frameworks = declaredFrameworks
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return frameworks.Length > 1 ? frameworks : Array.Empty<string>();
    }

    private static bool TryRestoreControlledMultiFrameworkProject(
        string originalProjectPath,
        ControlledPublishGraphNode representative,
        IReadOnlyCollection<string> frameworks,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot,
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
            argument.StartsWith("-p:TargetFrameworks=", StringComparison.OrdinalIgnoreCase));
        arguments.Add("-p:TargetFrameworks=" + BuildMsBuildListPropertyValue(frameworks));
        arguments.Add("-p:BuildProjectReferences=false");
        arguments.Add("-p:RestoreRecursive=false");
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
        StringComparer pathComparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return nodes
            .GroupBy(node => Path.GetFullPath(node.Request.ProjectPath), pathComparer)
            .Any(group => group
                .Select(BuildControlledRestoreContextKey)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count() > 1);
    }

    private static string BuildControlledRestoreContextKey(ControlledPublishGraphNode node)
        => string.Join(
            "\n",
            node.Request.ReadEffectiveGlobalProperties()
                .Where(property =>
                    !property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) &&
                    !property.Key.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                .Select(property => property.Key.ToUpperInvariant() + "=" + property.Value));

    private static bool TryBuildControlledPublishGraphNode(
        ControlledPublishGraphNode node,
        bool restore,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot,
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
            "-target:Build"
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
        if (!TryBuildControlledPathMap(
                controlledSourceRoot,
                originalGitRoot,
                node.PathMap,
                out string controlledPathMap))
        {
            failureReason = $"PathMap for controlled project '{originalProjectPath}' could not be constructed.";
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
