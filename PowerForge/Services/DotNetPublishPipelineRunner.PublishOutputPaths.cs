namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string[] ResolvePlannedPublishGeneratedPaths(DotNetPublishPlan plan)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        foreach (DotNetPublishStep step in (plan.Steps ?? Array.Empty<DotNetPublishStep>())
                     .Where(static step => step is not null && step.Kind == DotNetPublishStepKind.Publish))
        {
            DotNetPublishTargetPlan target = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                .First(candidate => candidate.Name.Equals(
                    step.TargetName,
                    StringComparison.OrdinalIgnoreCase));
            string framework = string.IsNullOrWhiteSpace(step.Framework)
                ? target.Publish.Framework
                : step.Framework!.Trim();
            string runtime = step.Runtime ?? string.Empty;
            DotNetPublishStyle style = step.Style ?? target.Publish.Style;
            IReadOnlyDictionary<string, string> tokens = BuildPublishOutputTokens(
                plan,
                target,
                framework,
                runtime,
                style);
            string outputDirectory = ResolvePublishOutputDirectory(
                plan,
                target,
                framework,
                runtime,
                style);
            paths.Add(outputDirectory);
            if (target.Publish.Zip)
                paths.Add(ResolvePublishZipPath(outputDirectory, plan, target, tokens));
        }

        return paths.OrderBy(path => path, comparer).ToArray();
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

    private static string ResolvePublishZipPath(
        string outputDirectory,
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        IReadOnlyDictionary<string, string> tokens)
    {
        string nameTemplate = string.IsNullOrWhiteSpace(target.Publish.ZipNameTemplate)
            ? "{target}-{framework}-{rid}-{style}.zip"
            : target.Publish.ZipNameTemplate!;
        string zipName = ApplyTemplate(nameTemplate, tokens);
        if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            zipName += ".zip";

        string zipPath = string.IsNullOrWhiteSpace(target.Publish.ZipPath)
            ? Path.Combine(Path.GetDirectoryName(outputDirectory)!, zipName)
            : ResolvePath(plan.ProjectRoot, ApplyTemplate(target.Publish.ZipPath!, tokens));
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, zipPath, $"Target '{target.Name}' zip path");
        return Path.GetFullPath(zipPath);
    }
}
