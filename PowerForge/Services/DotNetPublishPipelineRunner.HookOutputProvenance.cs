namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static SourceProvenance ReadPortableInventorySourceProvenance(
        DotNetPublishPlan plan,
        string? outputDirectory = null,
        IEnumerable<string>? additionalGeneratedPaths = null)
    {
        string[] projectPaths = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Select(target => target.ProjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        string[] hookGeneratedOutputs = EnumerateCommandHookGeneratedOutputs(plan);
        string[] hookDeclaredOutputs = EnumerateCommandHookDeclaredOutputs(plan);
        IEnumerable<string> generatedPaths = EnumerateGeneratedProvenancePaths(
                plan,
                Array.Empty<DotNetPublishArtefactResult>(),
                Array.Empty<DotNetPublishStorePackageResult>(),
                Array.Empty<DotNetPublishMsiBuildResult>())
            .Concat(string.IsNullOrWhiteSpace(outputDirectory)
                ? Array.Empty<string>()
                : new[] { outputDirectory! })
            .Concat(additionalGeneratedPaths ?? Array.Empty<string>())
            .Concat(hookDeclaredOutputs);
        SourceProvenance provenance = ReadSourceProvenance(
            plan.ProjectRoot,
            generatedPaths,
            (plan.ConfigurationInputPaths ?? Array.Empty<string>())
                .Concat(projectPaths)
                .Where(path => !IsAdmittedCommandHookGeneratedInput(
                    plan.ProjectRoot,
                    path,
                    hookDeclaredOutputs,
                    hookGeneratedOutputs)),
            trustedExternalInputPaths: plan.GeneratedConfigurationInputPaths,
            buildProjectPaths: projectPaths,
            buildConfiguration: plan.Configuration,
            buildPlan: plan);
        if (string.IsNullOrWhiteSpace(provenance.Revision) ||
            !string.Equals(provenance.Revision, plan.SourceRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release source revision changed after planning; expected '{plan.SourceRevision ?? "unknown"}', " +
                $"received '{provenance.Revision ?? "unknown"}'.");
        }
        if (provenance.Dirty is not false)
        {
            string details = string.Join("; ", provenance.DirtyReasons
                .Concat(provenance.DirtyPaths.Select(path => "path: " + path)));
            throw new InvalidOperationException(
                "Release source changed after planning; portable signing is blocked before build or signing." +
                (string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details));
        }
        return provenance;
    }

    internal static string[] EnumerateCommandHookGeneratedOutputs(DotNetPublishPlan? plan)
        => EnumerateCommandHookOutputs(plan, requireValidated: true);

    internal static string[] EnumerateCommandHookDeclaredOutputs(DotNetPublishPlan? plan)
        => EnumerateCommandHookOutputs(plan, requireValidated: false);

    private static string[] EnumerateCommandHookOutputs(
        DotNetPublishPlan? plan,
        bool requireValidated)
    {
        if (plan is null || string.IsNullOrWhiteSpace(plan.ProjectRoot))
            return Array.Empty<string>();

        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var outputs = new HashSet<string>(comparison);
        foreach (DotNetPublishStep step in (plan.Steps ?? Array.Empty<DotNetPublishStep>())
                     .Where(step => step is not null &&
                                    step.Kind == DotNetPublishStepKind.CommandHook &&
                                    (!requireValidated || step.HookGeneratedOutputsValidated)))
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hook"] = step.HookId ?? string.Empty,
                ["phase"] = step.HookPhase?.ToString() ?? string.Empty,
                ["target"] = step.TargetName ?? string.Empty,
                ["rid"] = step.Runtime ?? string.Empty,
                ["framework"] = step.Framework ?? string.Empty,
                ["style"] = step.Style?.ToString() ?? string.Empty,
                ["bundle"] = step.BundleId ?? string.Empty,
                ["configuration"] = plan.Configuration,
                ["projectRoot"] = plan.ProjectRoot
            };
            foreach (string path in step.HookGeneratedOutputs ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                string output = ResolvePath(plan.ProjectRoot, ApplyTemplate(path, tokens));
                EnsurePathWithinRoot(plan.ProjectRoot, output, $"Hook '{step.HookId}' generated output");
                if (PathsEqual(plan.ProjectRoot, output))
                {
                    throw new InvalidOperationException(
                        $"Hook '{step.HookId}' generated output cannot be the project root.");
                }
                if (requireValidated && !PathEntryExists(output))
                {
                    throw new InvalidOperationException(
                        $"Hook '{step.HookId}' validated generated output no longer exists: {output}");
                }
                if (requireValidated)
                    EnsureHookGeneratedOutputTreeIsSafe(plan.ProjectRoot, step, output);
                outputs.Add(output);
            }
        }
        return outputs.OrderBy(path => path, comparison).ToArray();
    }

    private static bool IsAdmittedCommandHookGeneratedInput(
        string projectRoot,
        string path,
        IReadOnlyCollection<string> declaredOutputs,
        IReadOnlyCollection<string> validatedOutputs)
    {
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path));
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return declaredOutputs.Any(output =>
            (!PathEntryExists(output) || validatedOutputs.Any(validated =>
                string.Equals(validated, output, comparison))) &&
            IsSameOrBelowBuildInputPath(fullPath, output));
    }
}
