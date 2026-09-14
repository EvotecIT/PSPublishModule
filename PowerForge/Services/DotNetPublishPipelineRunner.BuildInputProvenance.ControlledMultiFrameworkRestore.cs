namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static Dictionary<string, ControlledPublishGraphNode[]> FindControlledMultiFrameworkProjects(
        IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes)
    {
        StringComparer pathComparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return graphBuildNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Request.TargetFramework))
            .GroupBy(node => Path.GetFullPath(node.Request.ProjectPath), pathComparer)
            .Select(group => new
            {
                ProjectPath = group.Key,
                Nodes = group.ToArray(),
                FrameworkCount = group
                    .Select(node => node.Request.TargetFramework)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()
            })
            .Where(group => group.FrameworkCount > 1)
            .ToDictionary(group => group.ProjectPath, group => group.Nodes, pathComparer);
    }

    private static bool TryRestoreControlledMultiFrameworkProject(
        string originalProjectPath,
        IReadOnlyCollection<ControlledPublishGraphNode> nodes,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot,
        out string? failureReason)
    {
        failureReason = null;
        if (!HaveCompatibleMultiFrameworkRestoreContexts(nodes))
        {
            failureReason = $"project '{originalProjectPath}' requires incompatible multi-framework restore contexts.";
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

        ControlledPublishGraphNode representative = nodes.First();
        string[] frameworks = nodes
            .Select(node => node.Request.TargetFramework)
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Select(framework => framework!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static bool HaveCompatibleMultiFrameworkRestoreContexts(
        IReadOnlyCollection<ControlledPublishGraphNode> nodes)
        => nodes
            .Select(node => string.Join(
                "\n",
                node.Request.ReadEffectiveGlobalProperties()
                    .Where(property =>
                        !property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) &&
                        !property.Key.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(property => property.Key.ToUpperInvariant() + "=" + property.Value)))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count() <= 1;
}
