namespace PowerForge;

/// <summary>
/// High-level pipeline step kinds used for progress reporting and UI rendering.
/// </summary>
public enum ModulePipelineStepKind
{
    /// <summary>Prepare release or project version metadata.</summary>
    Versioning = 10,
    /// <summary>Build module into staging.</summary>
    Build = 0,
    /// <summary>Generate documentation (markdown + external help).</summary>
    Documentation = 1,
    /// <summary>Format PowerShell sources.</summary>
    Formatting = 7,
    /// <summary>Sign module output (Authenticode).</summary>
    Signing = 8,
    /// <summary>Run validation checks (compatibility, consistency, module validation).</summary>
    Validation = 6,
    /// <summary>Run test suites.</summary>
    Tests = 9,
    /// <summary>Create an artefact output (packed/unpacked).</summary>
    Artefact = 2,
    /// <summary>Publish to a repository or GitHub.</summary>
    Publish = 3,
    /// <summary>Install the module into module roots.</summary>
    Install = 4,
    /// <summary>Delete generated staging directory.</summary>
    Cleanup = 5,
    /// <summary>Run a configured lifecycle action.</summary>
    Action = 11,
    /// <summary>Build repository packages before the module lane.</summary>
    PackageBuild = 12,
    /// <summary>Prepare external files before module staging.</summary>
    ExternalAsset = 13,
}

/// <summary>
/// Represents a planned pipeline step (for progress reporting and UI rendering).
/// </summary>
public sealed class ModulePipelineStep
{
    /// <summary>Step kind.</summary>
    public ModulePipelineStepKind Kind { get; }

    /// <summary>
    /// Stable key for the step within a single planned run. Intended for mapping UI tasks.
    /// </summary>
    public string Key { get; }

    /// <summary>Human-readable step title.</summary>
    public string Title { get; }

    /// <summary>Optional artefact segment associated with the step.</summary>
    public ConfigurationArtefactSegment? ArtefactSegment { get; }

    /// <summary>Optional publish segment associated with the step.</summary>
    public ConfigurationPublishSegment? PublishSegment { get; }

    /// <summary>Optional Xcode project version segment associated with the step.</summary>
    public ConfigurationXcodeProjectVersionSegment? XcodeProjectVersionSegment { get; }

    /// <summary>Optional Apple app segment associated with the step.</summary>
    public ConfigurationAppleAppSegment? AppleAppSegment { get; }

    /// <summary>Optional project-build JSON segment associated with the step.</summary>
    public ConfigurationProjectBuildSegment? ProjectBuildSegment { get; }

    /// <summary>Optional inline package-build segment associated with the step.</summary>
    public ConfigurationPackageBuildSegment? PackageBuildSegment { get; }

    /// <summary>Optional external asset segment associated with the step.</summary>
    public ConfigurationExternalAssetSegment? ExternalAssetSegment { get; }

    /// <summary>Optional lifecycle action associated with the step.</summary>
    public ConfigurationActionSegment? ActionSegment { get; }

    private ModulePipelineStep(
        ModulePipelineStepKind kind,
        string key,
        string title,
        ConfigurationArtefactSegment? artefactSegment = null,
        ConfigurationPublishSegment? publishSegment = null,
        ConfigurationXcodeProjectVersionSegment? xcodeProjectVersionSegment = null,
        ConfigurationAppleAppSegment? appleAppSegment = null,
        ConfigurationProjectBuildSegment? projectBuildSegment = null,
        ConfigurationPackageBuildSegment? packageBuildSegment = null,
        ConfigurationExternalAssetSegment? externalAssetSegment = null,
        ConfigurationActionSegment? actionSegment = null)
    {
        Kind = kind;
        Key = key ?? string.Empty;
        Title = title ?? string.Empty;
        ArtefactSegment = artefactSegment;
        PublishSegment = publishSegment;
        XcodeProjectVersionSegment = xcodeProjectVersionSegment;
        AppleAppSegment = appleAppSegment;
        ProjectBuildSegment = projectBuildSegment;
        PackageBuildSegment = packageBuildSegment;
        ExternalAssetSegment = externalAssetSegment;
        ActionSegment = actionSegment;
    }

    /// <summary>
    /// Creates the ordered list of steps for a given pipeline plan.
    /// </summary>
    public static ModulePipelineStep[] Create(ModulePipelinePlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var steps = new List<ModulePipelineStep>();

        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeDependencies);
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterDependencies);
        AddPackageBuildSteps(steps, plan);
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeVersioning);
        if (plan.AppleApps is { Length: > 0 })
        {
            for (int i = 0; i < plan.AppleApps.Length; i++)
            {
                var app = plan.AppleApps[i];
                if (app is null) continue;

                var cfg = app.Configuration ?? new AppleAppConfiguration();
                var label = "Prepare Apple app";
                if (!string.IsNullOrWhiteSpace(cfg.Name))
                    label += $" ({cfg.Name})";
                else if (!string.IsNullOrWhiteSpace(cfg.ProjectPath))
                    label += $" ({Path.GetFileName(cfg.ProjectPath)})";

                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Versioning,
                    key: $"version:apple:{i + 1:00}",
                    title: label,
                    appleAppSegment: app));
            }
        }

        if (plan.XcodeProjectVersions is { Length: > 0 })
        {
            for (int i = 0; i < plan.XcodeProjectVersions.Length; i++)
            {
                var x = plan.XcodeProjectVersions[i];
                if (x is null) continue;

                var path = x.Configuration?.Path;
                var label = "Update Xcode project version";
                if (!string.IsNullOrWhiteSpace(path)) label += $" ({Path.GetFileName(path)})";

                var key = $"version:xcode:{i + 1:00}";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Versioning,
                    key: key,
                    title: label,
                    xcodeProjectVersionSegment: x));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterVersioning);

        // 1) Build (always) - split into sub-steps for better progress visibility.
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeStaging);
        AddExternalAssetSteps(steps, plan);
        steps.Add(new ModulePipelineStep(
            kind: ModulePipelineStepKind.Build,
            key: "build:stage",
            title: "Stage to staging"));
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterStaging);
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeBuild);
        steps.Add(new ModulePipelineStep(
            kind: ModulePipelineStepKind.Build,
            key: "build:build",
            title: "Build module"));
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterBuild);
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeManifest);
        steps.Add(new ModulePipelineStep(
            kind: ModulePipelineStepKind.Build,
            key: "build:manifest",
            title: "Patch manifest"));
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterManifest);

        // 2) Docs (split into extraction + writing so users can see where time goes)
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeDocumentation);
        if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Documentation,
                key: "docs:extract",
                title: "Extract help"));
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Documentation,
                key: "docs:write",
                title: "Write docs"));

            if (plan.DocumentationBuild?.GenerateExternalHelp == true)
            {
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Documentation,
                    key: "docs:maml",
                    title: "Generate external help"));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterDocumentation);

        // 3) Formatting (after build/docs, before validation/packaging).
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeFormatting);
        if (plan.Formatting is not null)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Formatting,
                key: "format:staging",
                title: "Format PowerShell"));

            if (plan.Formatting.Options.UpdateProjectRoot)
            {
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Formatting,
                    key: "format:project",
                    title: "Format PowerShell (project)"));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterFormatting);

        // 4) Validation checks (after build/docs/formatting, before tests/packaging/publish/install).
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeValidation);
        if (plan.FileConsistencySettings?.Enable == true)
        {
            var scope = plan.FileConsistencySettings.ResolveScope();
            if (scope != FileConsistencyScope.ProjectOnly)
            {
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Validation,
                    key: "validate:fileconsistency",
                    title: "Check file consistency"));
            }

            if (scope != FileConsistencyScope.StagingOnly)
            {
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Validation,
                    key: "validate:fileconsistency-project",
                    title: "Check file consistency (project)"));
            }
        }

        if (plan.CompatibilitySettings?.Enable == true)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Validation,
                key: "validate:compatibility",
                title: "Check PowerShell compatibility"));
        }

        if (plan.ValidationSettings?.Enable == true)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Validation,
                key: "validate:module",
                title: "Validate module"));
        }

        if (plan.ImportModules?.RequiredModules == true &&
            plan.ImportModules.AnalyzeBinaryConflicts != false)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Validation,
                key: "validate:binary-conflicts",
                title: "Analyze binary conflicts"));
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterValidation);

        // 5) Tests (after validation, before signing/packaging/publish/install).
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeTests);
        if (plan.ImportModules is not null &&
            (plan.ImportModules.Self == true || plan.ImportModules.RequiredModules == true))
        {
            if (plan.ImportModules.Self == true && plan.ImportModules.SkipBinaryDependencyCheck != true)
            {
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Tests,
                    key: "tests:binary-dependencies",
                    title: "Check binary dependencies"));
            }

            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Tests,
                key: "tests:import-modules",
                title: "Import modules"));
        }

        if (plan.TestsAfterMerge is { Length: > 0 })
        {
            for (int i = 0; i < plan.TestsAfterMerge.Length; i++)
            {
                var key = $"tests:{i + 1:00}:aftermerge";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Tests,
                    key: key,
                    title: "Run tests"));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterTests);

        // 6) Signing (after tests, before packaging/publish/install).
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeSigning);
        if (plan.SignModule)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Signing,
                key: "sign",
                title: "Sign module"));
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterSigning);

        // 7) Artefacts
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeArtefacts);
        if (plan.Artefacts is { Length: > 0 })
        {
            for (int i = 0; i < plan.Artefacts.Length; i++)
            {
                var a = plan.Artefacts[i];
                if (a is null) continue;

                var id = a.Configuration?.ID;
                var label = $"Pack {a.ArtefactType}";
                if (!string.IsNullOrWhiteSpace(id)) label += $" ({id})";

                var key = $"artefact:{i + 1:00}:{a.ArtefactType}:{(id ?? string.Empty)}";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Artefact,
                    key: key,
                    title: label,
                    artefactSegment: a));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterArtefacts);

        // 8) Publishes
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforePublish);
        if (plan.Publishes is { Length: > 0 })
        {
            for (int i = 0; i < plan.Publishes.Length; i++)
            {
                var p = plan.Publishes[i];
                if (p is null) continue;

                var cfg = p.Configuration ?? new PublishConfiguration();
                var id = cfg.ID;
                var repoName = cfg.Repository?.Name ?? cfg.RepositoryName;

                var label = $"Publish {cfg.Destination}";
                if (!string.IsNullOrWhiteSpace(repoName)) label += $" ({repoName})";
                if (!string.IsNullOrWhiteSpace(id)) label += $" [{id}]";

                var key = $"publish:{i + 1:00}:{cfg.Destination}:{(id ?? string.Empty)}";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Publish,
                    key: key,
                    title: label,
                    publishSegment: p));
            }
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterPublish);

        // 9) Install
        AddActionSteps(steps, plan, ModulePipelineActionStage.BeforeInstall);
        if (plan.InstallEnabled)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Install,
                key: "install",
                title: $"Install ({plan.InstallStrategy}, keep {plan.InstallKeepVersions})"));
        }
        AddActionSteps(steps, plan, ModulePipelineActionStage.AfterInstall);

        // 10) Cleanup staging
        if (plan.DeleteGeneratedStagingAfterRun)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Cleanup,
                key: "cleanup",
                title: "Cleanup staging"));
        }

        return steps.ToArray();
    }

    private static void AddActionSteps(List<ModulePipelineStep> steps, ModulePipelinePlan plan, ModulePipelineActionStage stage)
    {
        var matches = (plan.Actions ?? Array.Empty<ConfigurationActionSegment>())
            .Where(action => action is not null && action.Configuration?.Enabled == true && action.Configuration.At == stage)
            .ToArray();

        for (var i = 0; i < matches.Length; i++)
        {
            var action = matches[i];
            var name = action.Configuration?.Name;
            var label = string.IsNullOrWhiteSpace(name) ? stage.ToString() : name!.Trim();
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Action,
                key: $"action:{stage}:{i + 1:00}",
                title: $"Run action ({label})",
                actionSegment: action));
        }
    }

    private static void AddPackageBuildSteps(List<ModulePipelineStep> steps, ModulePipelinePlan plan)
    {
        if (plan.ProjectBuilds is { Length: > 0 })
        {
            for (var i = 0; i < plan.ProjectBuilds.Length; i++)
            {
                var segment = plan.ProjectBuilds[i];
                if (segment?.Configuration?.BuildBeforeModule != true)
                    continue;

                var name = segment.Configuration.Name;
                var label = "Build packages";
                if (!string.IsNullOrWhiteSpace(name))
                    label += $" ({name})";
                else if (!string.IsNullOrWhiteSpace(segment.Configuration.ConfigPath))
                    label += $" ({Path.GetFileName(segment.Configuration.ConfigPath)})";

                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.PackageBuild,
                    key: $"package:project:{i + 1:00}",
                    title: label,
                    projectBuildSegment: segment));
            }
        }

        if (plan.PackageBuilds is { Length: > 0 })
        {
            for (var i = 0; i < plan.PackageBuilds.Length; i++)
            {
                var segment = plan.PackageBuilds[i];
                if (segment?.Configuration?.BuildBeforeModule != true)
                    continue;

                var name = segment.Configuration.Name;
                var label = string.IsNullOrWhiteSpace(name)
                    ? "Build packages (inline)"
                    : $"Build packages ({name})";

                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.PackageBuild,
                    key: $"package:inline:{i + 1:00}",
                    title: label,
                    packageBuildSegment: segment));
            }
        }
    }

    private static void AddExternalAssetSteps(List<ModulePipelineStep> steps, ModulePipelinePlan plan)
    {
        if (plan.ExternalAssets is not { Length: > 0 })
            return;

        for (var i = 0; i < plan.ExternalAssets.Length; i++)
        {
            var segment = plan.ExternalAssets[i];
            if (segment is null)
                continue;

            var name = segment.Configuration?.Name;
            var label = string.IsNullOrWhiteSpace(name)
                ? "Prepare external asset"
                : $"Prepare external asset ({name})";

            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.ExternalAsset,
                key: $"asset:external:{i + 1:00}",
                title: label,
                externalAssetSegment: segment));
        }
    }
}
