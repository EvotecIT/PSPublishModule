namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string[] ResolvePlannedPublishOutputDirectories(DotNetPublishPlan plan)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return (plan.Steps ?? Array.Empty<DotNetPublishStep>())
            .Where(static step => step is not null && step.Kind == DotNetPublishStepKind.Publish)
            .Select(step =>
            {
                DotNetPublishTargetPlan target = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                    .First(candidate => candidate.Name.Equals(
                        step.TargetName,
                        StringComparison.OrdinalIgnoreCase));
                string framework = string.IsNullOrWhiteSpace(step.Framework)
                    ? target.Publish.Framework
                    : step.Framework!.Trim();
                DotNetPublishStyle style = step.Style ?? target.Publish.Style;
                return ResolvePublishOutputDirectory(
                    plan,
                    target,
                    framework,
                    step.Runtime ?? string.Empty,
                    style);
            })
            .Distinct(comparer)
            .ToArray();
    }

    private static string ResolvePublishOutputDirectory(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        IReadOnlyDictionary<string, string> tokens = BuildPublishOutputTokens(
            plan,
            target,
            framework,
            runtime,
            style);
        string outputDirTemplate = string.IsNullOrWhiteSpace(target.Publish.OutputPath)
            ? Path.Combine("Artifacts", "DotNetPublish", "{target}", "{rid}", "{framework}", "{style}")
            : target.Publish.OutputPath!;
        string outputDir = ResolvePath(
            plan.ProjectRoot,
            ApplyTemplate(outputDirTemplate, tokens));
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, outputDir, $"Target '{target.Name}' output path");
        return outputDir;
    }

    private static IReadOnlyDictionary<string, string> BuildPublishOutputTokens(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target.Name,
            ["rid"] = runtime,
            ["framework"] = framework,
            ["style"] = style.ToString(),
            ["configuration"] = plan.Configuration,
            ["version"] = ResolvePublishReleaseVersion(
                plan,
                target.Name,
                framework,
                runtime,
                style) ?? string.Empty
        };
}
