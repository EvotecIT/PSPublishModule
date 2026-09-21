using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Portfolio;

/// <summary>Projects executable PowerForge plans into bounded, secret-safe review rows.</summary>
internal static class RepositoryPlanActionProjectionService
{
    private const int MaxActions = 200;
    private const int MaxTextLength = 240;

    public static IReadOnlyList<RepositoryPlanAction> FromModule(ModulePipelinePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var rows = new ActionBuilder();
        AddModule(rows, plan);
        return rows.Build();
    }

    private static void AddModule(ActionBuilder rows, ModulePipelinePlan plan)
    {
        foreach (var step in ModulePipelineStep.Create(plan))
        {
            var target = ModuleStepTarget(plan, step);
            var detail = ModuleStepDetail(plan, step);
            rows.Add(ModuleLane(step.Kind), step.Title, target, detail);
        }
    }

    public static IReadOnlyList<RepositoryPlanAction> FromProject(ProjectBuildHostExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureProjectPlan(execution.Result.Release, execution.ConfigPath);
        return FromProject(execution.Result.Release);
    }

    public static IReadOnlyList<RepositoryPlanAction> FromProjectPlanFile(string planPath)
    {
        if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath))
            throw new InvalidDataException("The generated project plan file was not found.");
        try
        {
            var plan = JsonSerializer.Deserialize<DotNetRepositoryReleaseResult>(File.ReadAllText(planPath), new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });
            EnsureProjectPlan(plan, planPath);
            return FromProject(plan);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The generated project plan is not valid PowerForge plan JSON.", ex);
        }
    }

    public static IReadOnlyList<RepositoryPlanAction> FromUnified(
        PowerForgeReleaseResult result,
        ModulePipelinePlan? modulePipelinePlan = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var rows = new ActionBuilder();

        if (result.ModulePlan is not null && modulePipelinePlan is null)
            throw new InvalidDataException("The unified module lane did not produce a reviewable canonical plan.");
        if (modulePipelinePlan is not null)
            AddModule(rows, modulePipelinePlan);

        AddProject(rows, result.Packages?.Result.Release, "NuGet");
        foreach (var packageLane in result.ModulePackagePlans)
            AddProject(rows, packageLane.Release, ValueOrFallback(packageLane.Name, "NuGet"));

        if (result.ToolPlan is { } toolPlan)
        {
            foreach (var target in toolPlan.Targets)
            {
                foreach (var combination in target.Combinations)
                {
                    var detail = $"{combination.Framework} / {combination.Runtime} / {combination.Flavor}";
                    rows.Add("Executable", "Publish executable", target.Name, detail);
                    if (!string.IsNullOrWhiteSpace(combination.ZipPath))
                        rows.Add("ZIP", "Create archive", combination.ZipPath, target.Name);
                }
            }
        }

        if (result.DotNetToolPlan is { } dotNetPlan)
        {
            foreach (var step in dotNetPlan.Steps)
            {
                var lane = LaneFor(step.Kind);
                var target = FirstValue(step.TargetName, step.InstallerId, step.StorePackageId, step.BundleId,
                    step.GateId, step.InstallerOutputPath, step.StorePackageOutputPath, step.BundleZipPath,
                    step.BundleOutputPath, step.Title);
                var detail = BuildStepDetail(step);
                rows.Add(lane, ValueOrFallback(step.Title, FriendlyAction(step.Kind)), target, detail);
            }
        }

        if (result.WingetSubmissionPlan is { Enabled: true } winget)
        {
            foreach (var entry in winget.Entries)
                rows.Add("WinGet", "Prepare manifest submission", $"{entry.PackageIdentifier} {entry.PackageVersion}",
                    $"{winget.Mode}; submission deferred to Releases");
        }
        else
        {
            foreach (var manifest in result.WingetManifests)
                rows.Add("WinGet", "Generate manifest", manifest.ManifestPath,
                    $"{manifest.PackageIdentifier} {manifest.PackageVersion}");
        }

        return rows.Build();
    }

    private static IReadOnlyList<RepositoryPlanAction> FromProject(DotNetRepositoryReleaseResult? release)
    {
        var rows = new ActionBuilder();
        AddProject(rows, release, "NuGet");
        return rows.Build();
    }

    private static void EnsureProjectPlan(DotNetRepositoryReleaseResult? release, string source)
    {
        if (release is null)
            throw new InvalidDataException($"The project plan from '{source}' did not contain a release plan.");
        if (release.Projects.Count == 0)
            throw new InvalidDataException($"The project plan from '{source}' did not contain any buildable projects.");
    }

    private static string ModuleStepTarget(ModulePipelinePlan plan, ModulePipelineStep step)
    {
        if (step.ActionSegment is { } action)
            return string.IsNullOrWhiteSpace(action.Configuration.FilePath)
                ? "Configured inline action"
                : Path.GetFileName(action.Configuration.FilePath);
        if (step.ArtefactSegment is { } artefact)
            return ValueOrFallback(artefact.Configuration.ArtefactName, artefact.Configuration.Path);
        if (step.ProjectBuildSegment is { } projectBuild)
            return ValueOrFallback(projectBuild.Configuration.Name, Path.GetFileName(projectBuild.Configuration.ConfigPath));
        if (step.PackageBuildSegment is { } packageBuild)
            return ValueOrFallback(packageBuild.Configuration.Name, "Inline package configuration");
        if (step.PublishSegment is { } publish)
            return ValueOrFallback(publish.Configuration.RepositoryName, publish.Configuration.Destination.ToString());
        return plan.ModuleName;
    }

    private static string ModuleStepDetail(ModulePipelinePlan plan, ModulePipelineStep step)
    {
        if (step.ActionSegment is { } action)
            return $"At {action.Configuration.At}; script contents and environment hidden";
        if (step.Kind == ModulePipelineStepKind.Build)
            return $"Version {plan.ResolvedVersion}; configuration {plan.BuildSpec.Configuration}";
        if (step.Kind == ModulePipelineStepKind.Publish)
            return "Deferred to Releases; credentials hidden";
        return $"Canonical module step {step.Key}";
    }

    private static string ModuleLane(ModulePipelineStepKind kind) => kind switch
    {
        ModulePipelineStepKind.Action => "PowerShell",
        ModulePipelineStepKind.PackageBuild => "NuGet",
        ModulePipelineStepKind.Artefact => "Artifact",
        ModulePipelineStepKind.Documentation => "Docs",
        ModulePipelineStepKind.Formatting => "Format",
        ModulePipelineStepKind.Validation => "Validate",
        ModulePipelineStepKind.Tests => "Tests",
        ModulePipelineStepKind.Signing => "Sign",
        ModulePipelineStepKind.Publish => "Release",
        ModulePipelineStepKind.Install => "Install",
        ModulePipelineStepKind.Cleanup => "Cleanup",
        ModulePipelineStepKind.Versioning => "Version",
        ModulePipelineStepKind.ExternalAsset => "Asset",
        _ => "Module"
    };

    private static void AddProject(ActionBuilder rows, DotNetRepositoryReleaseResult? release, string lane)
    {
        if (release is null) return;
        foreach (var project in release.Projects)
        {
            var target = ValueOrFallback(project.PackageId, project.ProjectName);
            var version = ValueOrFallback(project.NewVersion, ValueOrFallback(release.ResolvedVersion, "resolved at build"));
            rows.Add(lane, project.IsPackable ? "Build and pack project" : "Build project", target, $"Version {version}");
            foreach (var package in project.Packages)
                rows.Add(lane, "Create NuGet package", package, target);
            foreach (var symbols in project.SymbolPackages)
                rows.Add(lane, "Create symbols package", symbols, target);
            if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                rows.Add("ZIP", "Create release archive", project.ReleaseZipPath, target);
        }
    }

    private static string BuildStepDetail(DotNetPublishStep step)
    {
        var parts = new[] { step.Framework, step.Runtime, step.Style?.ToString(), step.HookPhase?.ToString() }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var detail = string.Join(" / ", parts);
        if (step.Kind == DotNetPublishStepKind.CommandHook)
            return string.IsNullOrWhiteSpace(detail) ? "Configured command; arguments hidden" : $"{detail}; arguments hidden";
        return string.IsNullOrWhiteSpace(detail) ? "Local plan step" : detail;
    }

    private static string LaneFor(DotNetPublishStepKind kind) => kind switch
    {
        DotNetPublishStepKind.Publish or DotNetPublishStepKind.Restore or DotNetPublishStepKind.Clean or DotNetPublishStepKind.Build => "Executable",
        DotNetPublishStepKind.Bundle => "ZIP",
        DotNetPublishStepKind.MsiPrepare or DotNetPublishStepKind.MsiBuild or DotNetPublishStepKind.MsiSign => "MSI",
        DotNetPublishStepKind.StorePackage => "MSIX",
        DotNetPublishStepKind.DebianPackage => "DEB",
        DotNetPublishStepKind.MacAppPackage => "macOS",
        DotNetPublishStepKind.CommandHook => "Command",
        DotNetPublishStepKind.BenchmarkExtract or DotNetPublishStepKind.BenchmarkGate => "Benchmark",
        _ => "Build"
    };

    private static string FriendlyAction(DotNetPublishStepKind kind)
        => string.Concat(kind.ToString().Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private static string FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Configured target";

    private static string ValueOrFallback(string? value, string? fallback)
        => !string.IsNullOrWhiteSpace(value) ? value : !string.IsNullOrWhiteSpace(fallback) ? fallback : "Configured target";

    private sealed class ActionBuilder
    {
        private readonly List<RepositoryPlanAction> _actions = [];
        private int _totalCount;

        public void Add(string lane, string action, string target, string detail)
        {
            _totalCount++;
            if (_actions.Count >= MaxActions) return;
            _actions.Add(new RepositoryPlanAction(
                _actions.Count + 1,
                Bound(lane),
                Bound(action),
                Bound(target),
                Bound(detail)));
        }

        public IReadOnlyList<RepositoryPlanAction> Build()
        {
            if (_totalCount <= MaxActions) return _actions;
            var omitted = _totalCount - (MaxActions - 1);
            _actions.RemoveAt(MaxActions - 1);
            _actions.Add(new RepositoryPlanAction(
                MaxActions,
                "Plan limit",
                "Additional actions omitted",
                $"{omitted} more action(s)",
                "Open the plan source for the complete list before execution."));
            return _actions;
        }

        private static string Bound(string value)
        {
            var safe = StudioOutputSanitizer.Sanitize(value).ReplaceLineEndings(" ").Trim();
            if (safe.Length == 0) return "Configured target";
            return safe.Length <= MaxTextLength ? safe : safe[..(MaxTextLength - 1)] + "…";
        }
    }
}
