using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly string[] EvaluatedBuildItemNames = CreateEvaluatedBuildItemNames();

    private static string[] CreateEvaluatedBuildItemNames()
        =>
        [
            "ProjectReference",
            "Compile",
            "Content",
            "EmbeddedResource",
            "AdditionalFiles",
            "Analyzer",
            "COMFileReference",
            "COMReference",
            "Reference",
            "ReferencePath",
            "ReferenceCopyLocalPaths",
            "NativeReference",
            "EditorConfigFiles",
            "GlobalAnalyzerConfigFiles",
            "ApplicationDefinition",
            "Page",
            "Resource",
            "SplashScreen",
            "RazorComponent",
            "TypeScriptCompile",
            "None"
        ];

    private static readonly HashSet<string> EvaluatedSourceItemNames = new(
    [
        "Compile",
        "Content",
        "EmbeddedResource",
        "AdditionalFiles",
        "Analyzer",
        "EditorConfigFiles",
        "GlobalAnalyzerConfigFiles",
        "ApplicationDefinition",
        "Page",
        "Resource",
        "SplashScreen",
        "RazorComponent",
        "TypeScriptCompile"
    ],
    StringComparer.Ordinal);

    internal static string[] EnumerateBundleSourceInputs(DotNetPublishPlan? plan)
    {
        if (plan is null || string.IsNullOrWhiteSpace(plan.ProjectRoot))
            return Array.Empty<string>();

        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inputs = new HashSet<string>(comparison);
        foreach (DotNetPublishBundlePlan bundle in plan.Bundles ?? Array.Empty<DotNetPublishBundlePlan>())
        {
            if (bundle is null)
                continue;

            foreach (DotNetPublishBundleScriptPlan script in bundle.Scripts ?? Array.Empty<DotNetPublishBundleScriptPlan>())
            {
                if (script is not null && !string.IsNullOrWhiteSpace(script.Path))
                    AddBundleSourceInput(
                        inputs,
                        ResolvePath(plan.ProjectRoot, script.Path),
                        required: true);
            }

            DotNetPublishStep[] steps = (plan.Steps ?? Array.Empty<DotNetPublishStep>())
                .Where(step => step is not null &&
                               step.Kind == DotNetPublishStepKind.Bundle &&
                               string.Equals(step.BundleId, bundle.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (steps.Length == 0)
            {
                DotNetPublishTargetPlan? sourceTarget = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                    .FirstOrDefault(target => string.Equals(
                        target.Name,
                        bundle.PrepareFromTarget,
                        StringComparison.OrdinalIgnoreCase));
                steps = (sourceTarget?.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                    .Where(combination => BundleMatchesCombo(bundle, combination))
                    .Select(combination => new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Bundle,
                        BundleId = bundle.Id,
                        TargetName = bundle.PrepareFromTarget,
                        Framework = combination.Framework,
                        Runtime = combination.Runtime,
                        Style = combination.Style
                    })
                    .ToArray();
            }

            foreach (DotNetPublishStep step in steps)
            {
                Dictionary<string, string> tokens = BuildBundleSourceInputTokens(plan, bundle, step);
                foreach (DotNetPublishBundleScriptPlan script in bundle.Scripts ?? Array.Empty<DotNetPublishBundleScriptPlan>())
                {
                    if (script is null)
                        continue;
                    string workingDirectory = string.IsNullOrWhiteSpace(script.WorkingDirectory)
                        ? plan.ProjectRoot
                        : ResolvePath(plan.ProjectRoot, ApplyTemplate(script.WorkingDirectory!, tokens));
                    foreach (string argument in script.Arguments ?? Array.Empty<string>())
                    {
                        AddFileBackedCommandValueSourceInput(
                            inputs,
                            ApplyTemplate(argument ?? string.Empty, tokens),
                            workingDirectory);
                    }
                }
                foreach (DotNetPublishBundleCopyItemPlan item in bundle.CopyItems ?? Array.Empty<DotNetPublishBundleCopyItemPlan>())
                {
                    if (item is null || string.IsNullOrWhiteSpace(item.SourcePath))
                        continue;
                    AddBundleSourceInput(
                        inputs,
                        ResolvePath(plan.ProjectRoot, ApplyTemplate(item.SourcePath, tokens)),
                        item.Required);
                }
                foreach (DotNetPublishBundleModuleIncludePlan module in bundle.ModuleIncludes ?? Array.Empty<DotNetPublishBundleModuleIncludePlan>())
                {
                    if (module is null || string.IsNullOrWhiteSpace(module.SourcePath))
                        continue;
                    Dictionary<string, string> moduleTokens = tokens.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value,
                        StringComparer.OrdinalIgnoreCase);
                    moduleTokens["moduleName"] = module.ModuleName;
                    AddBundleSourceInput(
                        inputs,
                        ResolvePath(plan.ProjectRoot, ApplyTemplate(module.SourcePath, moduleTokens)),
                        module.Required);
                }
                foreach (DotNetPublishBundleGeneratedScriptPlan generated in bundle.GeneratedScripts ?? Array.Empty<DotNetPublishBundleGeneratedScriptPlan>())
                {
                    if (generated is null || string.IsNullOrWhiteSpace(generated.TemplatePath))
                        continue;
                    AddBundleSourceInput(
                        inputs,
                        ResolvePath(plan.ProjectRoot, ApplyTemplate(generated.TemplatePath!, tokens)),
                        required: true);
                }
            }
        }

        return inputs.OrderBy(path => path, comparison).ToArray();
    }

    internal static string[] EnumerateCommandHookSourceInputs(DotNetPublishPlan? plan)
    {
        if (plan is null || string.IsNullOrWhiteSpace(plan.ProjectRoot))
            return Array.Empty<string>();

        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inputs = new HashSet<string>(comparison);
        foreach (DotNetPublishStep step in (plan.Steps ?? Array.Empty<DotNetPublishStep>())
                     .Where(step => step is not null && step.Kind == DotNetPublishStepKind.CommandHook))
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
            string command = ApplyTemplate(step.HookCommand ?? string.Empty, tokens);
            string commandPath = ResolveHookCommandPath(plan.ProjectRoot, command);
            AddCommandHookSourceInput(
                inputs,
                Path.IsPathRooted(commandPath) ? commandPath : Path.Combine(plan.ProjectRoot, commandPath));

            string workingDirectory = string.IsNullOrWhiteSpace(step.HookWorkingDirectory)
                ? plan.ProjectRoot
                : ResolvePath(plan.ProjectRoot, ApplyTemplate(step.HookWorkingDirectory!, tokens));
            foreach (string argument in step.HookArguments ?? Array.Empty<string>())
            {
                AddFileBackedCommandValueSourceInput(
                    inputs,
                    ApplyTemplate(argument ?? string.Empty, tokens),
                    workingDirectory);
            }
            foreach (string value in (step.HookEnvironment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)).Values)
            {
                AddFileBackedCommandValueSourceInput(
                    inputs,
                    ApplyTemplate(value ?? string.Empty, tokens),
                    workingDirectory);
            }
        }

        return inputs.OrderBy(path => path, comparison).ToArray();
    }

    private static void AddFileBackedCommandValueSourceInput(
        HashSet<string> inputs,
        string? value,
        string workingDirectory)
    {
        string candidate = TrimMatchingQuotes((value ?? string.Empty).Trim());
        int assignment = candidate.IndexOf('=');
        if (assignment >= 0 && assignment < candidate.Length - 1)
            candidate = TrimMatchingQuotes(candidate.Substring(assignment + 1).Trim());
        if (string.IsNullOrWhiteSpace(candidate))
            return;
        AddCommandHookSourceInput(
            inputs,
            Path.IsPathRooted(candidate) ? candidate : Path.Combine(workingDirectory, candidate));
    }

    private static void AddCommandHookSourceInput(HashSet<string> inputs, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;
        try
        {
            string path = Path.GetFullPath(candidate);
            if (File.Exists(path))
                inputs.Add(path);
        }
        catch
        {
            // Runtime command validation owns malformed command text. Provenance stays fail closed for files it can resolve.
        }
    }

    private static Dictionary<string, string> BuildBundleSourceInputTokens(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        DotNetPublishStep step)
    {
        string targetName = string.IsNullOrWhiteSpace(step.TargetName)
            ? bundle.PrepareFromTarget
            : step.TargetName!;
        DotNetPublishTargetPlan? sourceTarget = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .FirstOrDefault(target => string.Equals(
                target.Name,
                bundle.PrepareFromTarget,
                StringComparison.OrdinalIgnoreCase));
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundle"] = bundle.Id,
            ["target"] = targetName,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["configuration"] = plan.Configuration,
            ["projectRoot"] = plan.ProjectRoot,
            ["output"] = step.BundleOutputPath ?? string.Empty,
            ["zip"] = step.BundleZipPath ?? string.Empty,
            ["keepSymbols"] = (sourceTarget?.Publish?.KeepSymbols ?? false).ToString(),
            ["keepDocs"] = (sourceTarget?.Publish?.KeepDocs ?? false).ToString(),
            ["signEnabled"] = (sourceTarget?.Publish?.Sign?.Enabled ?? false).ToString()
        };
        string sourceOutputTemplate = string.IsNullOrWhiteSpace(sourceTarget?.Publish?.OutputPath)
            ? Path.Combine("Artifacts", "DotNetPublish", "{target}", "{rid}", "{framework}", "{style}")
            : sourceTarget!.Publish.OutputPath!;
        tokens["sourceOutput"] = ResolvePath(plan.ProjectRoot, ApplyTemplate(sourceOutputTemplate, tokens));
        tokens["primaryOutput"] = string.IsNullOrWhiteSpace(tokens["output"]) ||
                                  string.IsNullOrWhiteSpace(bundle.PrimarySubdirectory)
            ? tokens["output"]
            : ResolvePath(tokens["output"], bundle.PrimarySubdirectory!);
        return tokens;
    }

    private static void AddBundleSourceInput(HashSet<string> inputs, string path, bool required)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            inputs.Add(fullPath);
            return;
        }
        if (Directory.Exists(fullPath))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                    inputs.Add(Path.GetFullPath(file));
            }
            catch
            {
                inputs.Add(Path.Combine(fullPath, ".powerforge-provenance-unreadable"));
            }
            return;
        }
        if (required)
            inputs.Add(fullPath);
    }

    private static bool TryEvaluateDotNetBuildInputs(
        IEnumerable<string>? projectPaths,
        string? configuration,
        DotNetPublishPlan? buildPlan,
        out string[] projectDirectories,
        out HashSet<string> buildInputs,
        out HashSet<string> sourceInputs,
        out NoBuildPublishInput[] noBuildPublishInputs)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ProjectEvaluationRequest[] roots = BuildProjectEvaluationRequests(
                projectPaths,
                configuration,
                buildPlan)
            .ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(comparison);
        var outputRootsByEvaluation = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var generatedRootsByEvaluation = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var expectedOutputPathsByEvaluation = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var projectDirectoriesByEvaluation = new Dictionary<string, string>(StringComparer.Ordinal);
        var buildInputsByEvaluation = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var msBuildInputsByEvaluation = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pathMapsByEvaluation = new Dictionary<string, string?>(StringComparer.Ordinal);
        var controlledGeneratedOutputProofs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var verifiedPackagesByEvaluation = new Dictionary<string, VerifiedPackageInputCatalog?>(StringComparer.Ordinal);
        var requestsByEvaluation = new Dictionary<string, ProjectEvaluationRequest>(StringComparer.Ordinal);
        var evaluationsByEvaluation = new Dictionary<string, EvaluatedProjectInputs>(StringComparer.Ordinal);
        var trustedBuildInfrastructureRootsByEvaluation =
            new Dictionary<string, string[]>(StringComparer.Ordinal);
        var generatedProjectReferenceOutputs = new List<(ProjectEvaluationRequest Request, GeneratedProjectReferenceOutput Output)>();
        var evaluatedPublishInputs = new List<(string EvaluationKey, EvaluatedPublishInput Input)>();
        using var verifiedPackageArchives = new VerifiedPackageArchiveCache();
        buildInputs = new HashSet<string>(comparison);
        sourceInputs = new HashSet<string>(comparison);
        noBuildPublishInputs = Array.Empty<NoBuildPublishInput>();

        foreach (ProjectEvaluationRequest root in roots)
        {
            if (!TryRefreshLockedRestoreOutputs(root))
            {
                projectDirectories = roots
                    .Select(request => Path.GetDirectoryName(request.ProjectPath)!)
                    .Distinct(comparison)
                    .ToArray();
                return false;
            }

            var pending = new Queue<ProjectEvaluationRequest>(new[] { root });
            while (pending.Count > 0)
            {
                ProjectEvaluationRequest request = pending.Dequeue();
                string visitKey = request.BuildVisitKey();
                if (!visited.Add(visitKey) || !File.Exists(request.ProjectPath))
                    continue;

                string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
                directories.Add(projectDirectory);
                buildInputs.Add(request.ProjectPath);
                sourceInputs.Add(request.ProjectPath);
                if (!TryReadEvaluatedProjectInputs(
                        request,
                        verifiedPackageArchives,
                        out EvaluatedProjectInputs? evaluation) || evaluation is null)
                {
                    projectDirectories = directories.ToArray();
                    return false;
                }

                foreach (string input in evaluation.BuildInputs)
                    buildInputs.Add(input);
                foreach (string input in evaluation.SourceInputs)
                    sourceInputs.Add(input);
                outputRootsByEvaluation[visitKey] = evaluation.OutputRoots;
                generatedRootsByEvaluation[visitKey] = evaluation.OutputRoots
                    .Concat(new[] { evaluation.IntermediateRoot, evaluation.IntermediateOutputPath })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(comparison)
                    .ToArray();
                expectedOutputPathsByEvaluation[visitKey] = evaluation.ExpectedOutputPaths;
                projectDirectoriesByEvaluation[visitKey] = projectDirectory;
                buildInputsByEvaluation[visitKey] = evaluation.BuildInputs
                    .Concat(new[] { request.ProjectPath })
                    .Distinct(comparison)
                    .ToArray();
                msBuildInputsByEvaluation[visitKey] = evaluation.MsBuildInputs;
                pathMapsByEvaluation[visitKey] = evaluation.PathMap;
                verifiedPackagesByEvaluation[visitKey] = evaluation.VerifiedPackages;
                requestsByEvaluation[visitKey] = request;
                evaluationsByEvaluation[visitKey] = evaluation;
                trustedBuildInfrastructureRootsByEvaluation[visitKey] =
                    evaluation.TrustedBuildInfrastructureRoots;
                generatedProjectReferenceOutputs.AddRange(
                    evaluation.GeneratedProjectReferenceOutputs.Select(output => (request, output)));
                if (string.IsNullOrEmpty(request.TargetFramework))
                {
                    if (evaluation.TargetFrameworks.Length > 0)
                    {
                        foreach (string targetFramework in evaluation.TargetFrameworks)
                            pending.Enqueue(request.ForProject(request.ProjectPath, targetFramework));
                    }
                    if (request.TargetFramework is not null || evaluation.TargetFrameworks.Length == 0)
                    {
                        foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                            pending.Enqueue(request.ForProject(projectReference));
                    }
                }
                else
                {
                    foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                        pending.Enqueue(request.ForProject(projectReference));
                }
            }
        }

        foreach (KeyValuePair<string, ProjectEvaluationRequest> entry in requestsByEvaluation)
        {
            string evaluationKey = entry.Key;
            ProjectEvaluationRequest request = entry.Value;
            if (string.IsNullOrWhiteSpace(request.TargetFramework) ||
                !TryReadFrozenProjectReferenceGraph(
                    request,
                    requestsByEvaluation,
                    evaluationsByEvaluation,
                    pathMapsByEvaluation,
                    out ControlledPublishGraphNode[] graphNodes,
                    out string[] graphEvaluationKeys))
            {
                if (!string.IsNullOrWhiteSpace(request.TargetFramework))
                {
                    projectDirectories = directories.ToArray();
                    return false;
                }
                continue;
            }

            string[] graphBuildInputs = graphEvaluationKeys
                .SelectMany(key => buildInputsByEvaluation[key])
                .Distinct(comparison)
                .ToArray();
            string[] graphMsBuildInputs = graphEvaluationKeys
                .SelectMany(key => msBuildInputsByEvaluation[key])
                .Distinct(comparison)
                .ToArray();
            string[] graphTrustedRoots = graphEvaluationKeys
                .SelectMany(key => trustedBuildInfrastructureRootsByEvaluation[key])
                .Distinct(comparison)
                .ToArray();
            VerifiedPackageInputCatalog[] graphPackages = graphEvaluationKeys
                .Select(key => verifiedPackagesByEvaluation[key])
                .OfType<VerifiedPackageInputCatalog>()
                .Distinct()
                .ToArray();
            if (!TryReadEvaluatedPublishInputs(
                    request,
                    verifiedPackagesByEvaluation[evaluationKey],
                    graphPackages,
                    graphTrustedRoots,
                    graphBuildInputs,
                    graphMsBuildInputs,
                    pathMapsByEvaluation[evaluationKey],
                    buildPlan?.NoBuildInPublish == true,
                    graphNodes,
                    out EvaluatedPublishInput[] publishInputs))
            {
                projectDirectories = directories.ToArray();
                return false;
            }
            evaluatedPublishInputs.AddRange(
                publishInputs.Select(input => (evaluationKey, input)));
        }

        var provenNoBuildPublishInputsByEvaluation =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var provenNoBuildPublishInputs = new List<NoBuildPublishInput>();
        bool IsTrustedSdkGeneratedOutput(string path)
            => generatedRootsByEvaluation.Keys.Any(ownerKey =>
                IsTrustedSdkGeneratedPublishInput(
                    path,
                    generatedRootsByEvaluation[ownerKey],
                    projectDirectoriesByEvaluation[ownerKey],
                    directories));
        if (buildPlan?.NoBuildInPublish == true)
        {
            foreach (IGrouping<string, (string EvaluationKey, EvaluatedPublishInput Input)> group in
                     evaluatedPublishInputs.GroupBy(entry => entry.EvaluationKey, StringComparer.Ordinal))
            {
                string evaluationKey = group.Key;
                string[] trustedInputs = group
                    .Where(entry => entry.Input.IsSdkDefined &&
                        entry.Input.IsControlledEquivalent &&
                        IsTrustedSdkGeneratedOutput(entry.Input.FullPath))
                    .Select(entry => entry.Input.FullPath)
                    .Distinct(comparison)
                    .ToArray();
                if (trustedInputs.Length == 0)
                {
                    continue;
                }

                provenNoBuildPublishInputsByEvaluation[evaluationKey] =
                    new HashSet<string>(trustedInputs, comparison);
                provenNoBuildPublishInputs.AddRange(group
                    .Where(entry => entry.Input.IsSdkDefined &&
                        entry.Input.IsControlledEquivalent &&
                        !string.IsNullOrWhiteSpace(entry.Input.ControlledSha256) &&
                        IsTrustedSdkGeneratedOutput(entry.Input.FullPath))
                    .Select(entry => new NoBuildPublishInput(
                        evaluationKey,
                        entry.Input.FullPath,
                        entry.Input.RelativePath,
                        entry.Input.Metadata,
                        entry.Input.ControlledSha256!)));
            }
        }

        foreach ((string evaluationKey, EvaluatedPublishInput publishInput) in evaluatedPublishInputs)
        {
            bool trustedGeneratedOutput = publishInput.IsSdkDefined &&
                IsTrustedSdkGeneratedOutput(publishInput.FullPath) &&
                (buildPlan?.NoBuildInPublish != true ||
                 (provenNoBuildPublishInputsByEvaluation.TryGetValue(
                      evaluationKey,
                      out HashSet<string>? provenInputs) &&
                  provenInputs.Contains(publishInput.FullPath)));
            if (trustedGeneratedOutput)
                continue;

            buildInputs.Add(publishInput.FullPath);
            if (File.Exists(publishInput.FullPath) &&
                !IsTrustedExternalBuildInfrastructurePath(publishInput.FullPath))
            {
                sourceInputs.Add(publishInput.FullPath);
            }
        }

        foreach ((ProjectEvaluationRequest request, GeneratedProjectReferenceOutput output) in generatedProjectReferenceOutputs)
        {
            ProjectEvaluationRequest referencedProject = request.ForProject(output.ProjectReference);
            if (outputRootsByEvaluation.TryGetValue(referencedProject.BuildVisitKey(), out string[]? outputRoots) &&
                expectedOutputPathsByEvaluation.TryGetValue(
                    referencedProject.BuildVisitKey(),
                    out string[]? expectedOutputPaths) &&
                IsTrustedGeneratedOutputPath(
                    output.OutputPath,
                    outputRoots,
                    expectedOutputPaths,
                    Path.GetDirectoryName(referencedProject.ProjectPath)!,
                    directories) &&
                TryProveControlledGeneratedOutputs(
                    referencedProject,
                    new[] { output.OutputPath },
                    buildInputsByEvaluation.TryGetValue(
                        referencedProject.BuildVisitKey(),
                        out string[]? evaluatedBuildInputs)
                        ? evaluatedBuildInputs
                        : Array.Empty<string>(),
                    msBuildInputsByEvaluation.TryGetValue(
                        referencedProject.BuildVisitKey(),
                        out string[]? evaluatedMsBuildInputs)
                        ? evaluatedMsBuildInputs
                        : Array.Empty<string>(),
                    pathMapsByEvaluation.TryGetValue(
                        referencedProject.BuildVisitKey(),
                        out string? pathMap)
                        ? pathMap
                        : null,
                    verifiedPackagesByEvaluation.TryGetValue(
                        referencedProject.BuildVisitKey(),
                        out VerifiedPackageInputCatalog? verifiedPackages)
                        ? verifiedPackages
                        : null,
                    controlledGeneratedOutputProofs))
            {
                continue;
            }

            buildInputs.Add(output.OutputPath);
            sourceInputs.Add(output.OutputPath);
        }

        noBuildPublishInputs = provenNoBuildPublishInputs
            .GroupBy(
                input => input.EvaluationKey + "\0" + input.FullPath + "\0" + input.RelativePath,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        projectDirectories = directories.ToArray();
        return true;
    }

    private static IEnumerable<ProjectEvaluationRequest> BuildProjectEvaluationRequests(
        IEnumerable<string>? projectPaths,
        string? configuration,
        DotNetPublishPlan? buildPlan)
    {
        string effectiveConfiguration = string.IsNullOrWhiteSpace(buildPlan?.Configuration)
            ? string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration!.Trim()
            : buildPlan!.Configuration.Trim();
        DotNetPublishTargetPlan[] targets = buildPlan?.Targets ?? Array.Empty<DotNetPublishTargetPlan>();
        if (targets.Length > 0)
        {
            foreach (DotNetPublishTargetPlan target in targets)
            {
                if (target is null || string.IsNullOrWhiteSpace(target.ProjectPath))
                    continue;
                DotNetPublishTargetCombination[] combinations = target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>();
                if (combinations.Length == 0)
                {
                    yield return new ProjectEvaluationRequest(
                        Path.GetFullPath(target.ProjectPath),
                        targetFramework: null,
                        effectiveConfiguration,
                        globalProperties: null,
                        buildPlan!.EnvironmentVariables);
                    continue;
                }

                foreach (DotNetPublishTargetCombination combination in combinations)
                {
                    Dictionary<string, string> properties = BuildPublishEvaluationProperties(
                        buildPlan!,
                        target,
                        combination);
                    yield return new ProjectEvaluationRequest(
                        Path.GetFullPath(target.ProjectPath),
                        combination.Framework,
                        effectiveConfiguration,
                        properties,
                        buildPlan!.EnvironmentVariables);
                }
            }

            yield break;
        }

        foreach (string path in projectPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            yield return new ProjectEvaluationRequest(
                Path.GetFullPath(path),
                targetFramework: null,
                effectiveConfiguration,
                globalProperties: null,
                environmentVariables: null);
        }
    }

    private static Dictionary<string, string> BuildPublishEvaluationProperties(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        DotNetPublishTargetCombination combination)
    {
        Dictionary<string, string> properties = BuildPublishMsBuildProperties(
            plan,
            target,
            combination.Framework,
            combination.Runtime,
            combination.Style);
        if (!string.IsNullOrWhiteSpace(combination.Runtime))
            properties["RuntimeIdentifier"] = combination.Runtime;

        if (IsPortableStyle(combination.Style))
        {
            properties["SelfContained"] = "true";
            properties["PublishSingleFile"] = "true";
            properties["IncludeNativeLibrariesForSelfExtract"] = "true";
            properties["PortableTrim"] = (combination.Style == DotNetPublishStyle.PortableSize).ToString().ToLowerInvariant();
            properties["PortableTrimMode"] = combination.Style == DotNetPublishStyle.PortableSize ? "full" : "partial";
            if (target.Publish.ReadyToRun.HasValue)
                properties["PublishReadyToRun"] = target.Publish.ReadyToRun.Value.ToString().ToLowerInvariant();
        }
        else if (combination.Style == DotNetPublishStyle.AotSpeed || combination.Style == DotNetPublishStyle.AotSize)
        {
            properties["SelfContained"] = "true";
            properties["PublishAot"] = "true";
            properties["StripSymbols"] = "true";
            properties["IlcOptimizationPreference"] = combination.Style == DotNetPublishStyle.AotSize ? "Size" : "Speed";
            properties["InvariantGlobalization"] = "false";
        }

        return properties;
    }

    private static bool TryReadEvaluatedProjectInputs(
        ProjectEvaluationRequest request,
        VerifiedPackageArchiveCache verifiedPackageArchives,
        out EvaluatedProjectInputs? evaluation)
    {
        evaluation = null;
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:MSBuildAllProjects",
            "-getProperty:BaseOutputPath",
            "-getProperty:OutputPath",
            "-getProperty:OutDir",
            "-getProperty:TargetDir",
            "-getProperty:TargetPath",
            "-getProperty:_GlobalPropertiesToRemoveFromProjectReferences",
            "-getProperty:BaseIntermediateOutputPath",
            "-getProperty:MSBuildProjectExtensionsPath",
            "-getProperty:IntermediateOutputPath",
            "-getProperty:PathMap",
            "-getProperty:NuGetPackageRoot",
            "-getProperty:NuGetPackageFolders",
            "-getProperty:ProjectAssetsFile",
            "-getProperty:NuGetLockFilePath",
            "-getProperty:MSBuildToolsPath",
            "-getProperty:MSBuildSDKsPath",
            "-getProperty:CustomAfterMicrosoftCommonTargets"
        };
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        foreach (string itemName in EvaluatedBuildItemNames)
            arguments.Add("-getItem:" + itemName);
        if (request.HasExplicitTargetFramework)
        {
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        }
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
        AddProjectReferenceExecutionProperties(
            arguments,
            request,
            preservePublishBuildProjectReferences: false);

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
            {
                return false;
            }

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                return false;
            }
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            JsonElement root = document.RootElement;
            var inputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var sourceInputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var references = new Dictionary<string, EvaluatedProjectReference>(StringComparer.Ordinal);
            var rawReferences = new Dictionary<string, EvaluatedProjectReference>(StringComparer.Ordinal);
            var publishEvaluatedReferences = new Dictionary<string, EvaluatedProjectReference>(StringComparer.Ordinal);
            var mainEvaluationReferenceKeys = new HashSet<string>(StringComparer.Ordinal);
            var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedBuildRoots = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var outputRoots = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var expectedOutputPaths = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var generatedProjectReferenceOutputs = new List<GeneratedProjectReferenceOutput>();
            EvaluatedPublishInput[] publishInputs = Array.Empty<EvaluatedPublishInput>();
            string? intermediateRoot = null;
            string? intermediateOutputPath = null;
            string? pathMap = null;
            string[] taskWideProjectReferencePropertyRemovals = Array.Empty<string>();
            IReadOnlyDictionary<string, string> evaluatedProjectReferenceConditionProperties =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var packageRoots = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var importPaths = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string[] trustedBuildInfrastructureRoots = Array.Empty<string>();
            PreprocessedProjectReferenceDeclaration[] projectReferenceDeclarations =
                Array.Empty<PreprocessedProjectReferenceDeclaration>();
            PreprocessedProjectPropertyDefinition[] preResolvePropertyDefinitions =
                Array.Empty<PreprocessedProjectPropertyDefinition>();
            bool hasDynamicProjectReferenceTaskOutputs = false;
            EvaluatedProjectItem[] dynamicProjectReferences = Array.Empty<EvaluatedProjectItem>();
            VerifiedPackageInputCatalog? verifiedPackages = null;
            string? msBuildToolsPath = null;
            string? msBuildSdksPath = null;
            string? customAfterMicrosoftCommonTargets = null;
            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                AddPropertyPath(properties, "BaseOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "OutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "BaseOutputPath", Path.GetDirectoryName(request.ProjectPath)!, outputRoots);
                AddPropertyPath(properties, "OutputPath", Path.GetDirectoryName(request.ProjectPath)!, outputRoots);
                AddPropertyPath(properties, "OutDir", Path.GetDirectoryName(request.ProjectPath)!, outputRoots);
                AddPropertyPath(properties, "TargetDir", Path.GetDirectoryName(request.ProjectPath)!, outputRoots);
                AddPropertyPath(properties, "TargetPath", Path.GetDirectoryName(request.ProjectPath)!, expectedOutputPaths);
                AddPropertyPath(properties, "BaseIntermediateOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "MSBuildProjectExtensionsPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "IntermediateOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                intermediateRoot = ReadEvaluatedPath(
                    properties,
                    "BaseIntermediateOutputPath",
                    Path.GetDirectoryName(request.ProjectPath)!) ??
                    ReadEvaluatedPath(
                        properties,
                        "MSBuildProjectExtensionsPath",
                        Path.GetDirectoryName(request.ProjectPath)!);
                intermediateOutputPath = ReadEvaluatedPath(
                    properties,
                    "IntermediateOutputPath",
                    Path.GetDirectoryName(request.ProjectPath)!);
                pathMap = ReadItemText(properties, "PathMap");
                taskWideProjectReferencePropertyRemovals = ReadProjectReferencePropertyNames(
                    ReadItemText(properties, "_GlobalPropertiesToRemoveFromProjectReferences"));
                AddPropertyPath(properties, "NuGetPackageRoot", Path.GetDirectoryName(request.ProjectPath)!, packageRoots);
                AddSemicolonSeparatedPathValues(
                    properties,
                    "NuGetPackageFolders",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    packageRoots);
                AddPackageFoldersFromAssets(
                    properties,
                    Path.GetDirectoryName(request.ProjectPath)!,
                    packageRoots);
                AddEffectiveBuildControlInputs(request.ProjectPath, properties, inputs, sourceInputs);
                AddSemicolonSeparatedValues(properties, "TargetFrameworks", targetFrameworks);
                if (targetFrameworks.Count == 0)
                    AddSemicolonSeparatedValues(properties, "TargetFramework", targetFrameworks);

                if (!VerifiedPackageInputCatalog.TryCreate(
                        request.ProjectPath,
                        properties,
                        packageRoots,
                        verifiedPackageArchives,
                        out verifiedPackages))
                {
                    return false;
                }
                trustedBuildInfrastructureRoots = ReadTrustedBuildInfrastructureRoots(
                    properties,
                    Path.GetDirectoryName(request.ProjectPath)!);
                msBuildToolsPath = ReadItemText(properties, "MSBuildToolsPath");
                msBuildSdksPath = ReadItemText(properties, "MSBuildSDKsPath");
                customAfterMicrosoftCommonTargets = ReadItemText(
                    properties,
                    "CustomAfterMicrosoftCommonTargets");
                AddSemicolonSeparatedPathValues(
                    properties,
                    "MSBuildAllProjects",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    importPaths);
                if (!TryReadPreprocessedProjectImports(
                        request,
                        out string[] preprocessedImports,
                        out projectReferenceDeclarations,
                        out preResolvePropertyDefinitions,
                        out hasDynamicProjectReferenceTaskOutputs,
                        out dynamicProjectReferences))
                {
                    return false;
                }
                importPaths.UnionWith(preprocessedImports);
                string[] evaluatedImportPaths = importPaths.ToArray();
                importPaths.UnionWith(ReadDeclaredBuildInputCandidates(
                    request.ProjectPath,
                    importPaths));
                if (verifiedPackages is not null &&
                    !verifiedPackages.TrySetControlledBuildInputs(evaluatedImportPaths))
                {
                    return false;
                }
                if (projectReferenceDeclarations.Length > 0 ||
                    hasDynamicProjectReferenceTaskOutputs ||
                    preResolvePropertyDefinitions.Any(definition =>
                        definition.Element.Name.LocalName.Equals(
                            "_GlobalPropertiesToRemoveFromProjectReferences",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    evaluatedProjectReferenceConditionProperties =
                        ReadEvaluatedProjectReferenceConditionProperties(request, importPaths);
                    if (!TryReadPreResolveTaskWideProjectReferencePropertyRemovals(
                            preResolvePropertyDefinitions,
                            evaluatedProjectReferenceConditionProperties,
                            out taskWideProjectReferencePropertyRemovals))
                    {
                        return false;
                    }
                }
                foreach (EvaluatedProjectItem dynamicProjectReference in dynamicProjectReferences)
                {
                    if (!TryReadEvaluatedProjectReferences(
                            dynamicProjectReference,
                            request.ProjectPath,
                            importPaths,
                            projectReferenceDeclarations,
                            evaluatedProjectReferenceConditionProperties,
                            taskWideProjectReferencePropertyRemovals,
                            out EvaluatedProjectReference[] itemReferences))
                    {
                        return false;
                    }
                    foreach (EvaluatedProjectReference rawReference in itemReferences)
                    {
                        string referenceKey = BuildEvaluatedProjectReferenceKey(rawReference);
                        publishEvaluatedReferences[referenceKey] = rawReference;
                        rawReferences[referenceKey] = rawReference;
                    }
                }
                foreach (string importPath in importPaths)
                {
                    AddClassifiedEvaluatedInput(
                        importPath,
                        isSourceInput: true,
                        inputs,
                        sourceInputs,
                        generatedBuildRoots,
                        verifiedPackages,
                        trustedBuildInfrastructureRoots);
                }

                if (root.TryGetProperty("Items", out JsonElement items))
                {
                    HashSet<string> embeddedResourceProjectReferences =
                        ReadProjectReferenceOutputKeys(items, "EmbeddedResource");
                    HashSet<string> analyzerProjectReferences =
                        ReadProjectReferenceOutputKeys(items, "Analyzer");
                    foreach (string itemName in EvaluatedBuildItemNames)
                    {
                        if (!items.TryGetProperty(itemName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                            continue;
                        if (IsAmbientReferenceResolutionItem(itemName) && values.GetArrayLength() > 0)
                        {
                            return false;
                        }
                        foreach (JsonElement item in values.EnumerateArray())
                        {
                            if (itemName.Equals("Reference", StringComparison.Ordinal) &&
                                TryResolveEvaluatedItemPath(
                                    item,
                                    "HintPath",
                                    Path.GetDirectoryName(request.ProjectPath)!,
                                    out string? hintPath))
                            {
                                if (!IsBelowGeneratedBuildRoot(hintPath!, generatedBuildRoots))
                                {
                                    AddClassifiedEvaluatedInput(
                                        hintPath!,
                                        isSourceInput: true,
                                        inputs,
                                        sourceInputs,
                                        generatedBuildRoots,
                                        verifiedPackages,
                                        trustedBuildInfrastructureRoots);
                                }
                            }

                            if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
                                fullPathElement.ValueKind != JsonValueKind.String ||
                                string.IsNullOrWhiteSpace(fullPathElement.GetString()))
                            {
                                continue;
                            }
                            string fullPath = Path.GetFullPath(fullPathElement.GetString()!);
                            if (itemName.Equals("ProjectReference", StringComparison.Ordinal))
                            {
                                inputs.Add(fullPath);
                                if (!TryReadEvaluatedProjectReferences(
                                        item,
                                        request.ProjectPath,
                                        importPaths,
                                        projectReferenceDeclarations,
                                        evaluatedProjectReferenceConditionProperties,
                                        taskWideProjectReferencePropertyRemovals,
                                        preferEffectiveLiteralAssignments: projectReferenceDeclarations.Any(
                                            declaration => declaration.IsTargetTime) ||
                                            hasDynamicProjectReferenceTaskOutputs,
                                        allowAmbiguousEvaluatedAssignments:
                                            hasDynamicProjectReferenceTaskOutputs,
                                        out EvaluatedProjectReference[] itemReferences) ||
                                    itemReferences.Length == 0)
                                {
                                    return false;
                                }
                                foreach (EvaluatedProjectReference rawReference in itemReferences)
                                {
                                    string referenceKey = BuildEvaluatedProjectReferenceKey(rawReference);
                                    mainEvaluationReferenceKeys.Add(referenceKey);
                                    rawReferences[referenceKey] = rawReference;
                                }
                                continue;
                            }
                            if (TryReadGeneratedProjectReferenceOutputs(
                                         itemName,
                                         fullPath,
                                         item,
                                         msBuildToolsPath,
                                         msBuildSdksPath,
                                         request.ProjectPath,
                                         importPaths,
                                         projectReferenceDeclarations,
                                         evaluatedProjectReferenceConditionProperties,
                                         embeddedResourceProjectReferences,
                                         analyzerProjectReferences,
                                         taskWideProjectReferencePropertyRemovals,
                                         rawReferences.Values,
                                         out GeneratedProjectReferenceOutput[] generatedOutputs))
                            {
                                generatedProjectReferenceOutputs.AddRange(generatedOutputs);
                                continue;
                            }
                            if (IsBelowGeneratedBuildRoot(fullPath, generatedBuildRoots))
                                continue;
                            if (itemName.Equals("EmbeddedResource", StringComparison.Ordinal) &&
                                TryResolveEvaluatedItemPath(
                                    item,
                                    "DependentUpon",
                                    Path.GetDirectoryName(fullPath)!,
                                    out string? dependentUponPath))
                            {
                                AddClassifiedEvaluatedInput(
                                    dependentUponPath!,
                                    isSourceInput: true,
                                    inputs,
                                    sourceInputs,
                                    generatedBuildRoots,
                                    verifiedPackages,
                                    trustedBuildInfrastructureRoots);
                            }
                            if (!itemName.Equals("None", StringComparison.Ordinal) || IsOutputRelevantNoneItem(item))
                            {
                                bool isSourceInput = EvaluatedSourceItemNames.Contains(itemName) ||
                                    (itemName.Equals("None", StringComparison.Ordinal) && IsOutputRelevantNoneItem(item));
                                AddClassifiedEvaluatedInput(
                                    fullPath,
                                    isSourceInput,
                                    inputs,
                                    sourceInputs,
                                    generatedBuildRoots,
                                    verifiedPackages,
                                    trustedBuildInfrastructureRoots);
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(request.TargetFramework))
            {
                foreach (EvaluatedProjectReference projectReference in rawReferences.Values)
                    references[BuildEvaluatedProjectReferenceKey(projectReference)] = projectReference;
            }
            else if (rawReferences.Count > 0 ||
                     projectReferenceDeclarations.Any(declaration =>
                         declaration.IsTargetTime &&
                         !IsTrustedExternalBuildInfrastructurePath(
                             declaration.DefiningProjectPath,
                             trustedBuildInfrastructureRoots) &&
                         declaration.Element.Attributes().Any(attribute =>
                             attribute.Name.LocalName.Equals(
                                 "Include",
                                 StringComparison.OrdinalIgnoreCase))) ||
                     hasDynamicProjectReferenceTaskOutputs)
            {
                if (!TryReadControlledResolvedProjectReferences(
                        request,
                        verifiedPackages,
                        inputs,
                        importPaths
                            .Concat(new[] { request.ProjectPath })
                            .Concat(rawReferences.Values.Select(reference => reference.ProjectPath))
                            .ToArray(),
                        rawReferences.Values.ToArray(),
                        taskWideProjectReferencePropertyRemovals,
                        projectReferenceDeclarations,
                        evaluatedProjectReferenceConditionProperties,
                        hasDynamicProjectReferenceTaskOutputs,
                        intermediateRoot,
                        customAfterMicrosoftCommonTargets,
                        out EvaluatedProjectReference[] resolvedReferences,
                        out string resolvedItemsJson))
                {
                    return false;
                }
                foreach (EvaluatedProjectReference reference in MergeResolvedProjectReferenceContexts(
                             rawReferences.Values,
                             resolvedReferences,
                             publishEvaluatedReferences.Values,
                             mainEvaluationReferenceKeys))
                {
                    references[BuildEvaluatedProjectReferenceKey(reference)] = reference;
                    inputs.Add(reference.ProjectPath);
                }
                using JsonDocument resolvedItemsDocument = JsonDocument.Parse(resolvedItemsJson);
                if (!TryProcessControlledProjectReferenceItems(
                        resolvedItemsDocument.RootElement,
                        request.ProjectPath,
                        msBuildToolsPath,
                        msBuildSdksPath,
                        importPaths,
                        projectReferenceDeclarations,
                        evaluatedProjectReferenceConditionProperties,
                        taskWideProjectReferencePropertyRemovals,
                        rawReferences.Values.Concat(resolvedReferences),
                        inputs,
                        sourceInputs,
                        generatedBuildRoots,
                        verifiedPackages,
                        trustedBuildInfrastructureRoots,
                        generatedProjectReferenceOutputs))
                {
                    return false;
                }
            }

            evaluation = new EvaluatedProjectInputs(
                inputs.ToArray(),
                importPaths.Concat(new[] { request.ProjectPath }).Distinct(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray(),
                sourceInputs.ToArray(),
                references.Values.ToArray(),
                targetFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                outputRoots.ToArray(),
                expectedOutputPaths.ToArray(),
                intermediateRoot,
                intermediateOutputPath,
                pathMap,
                generatedProjectReferenceOutputs.ToArray(),
                publishInputs,
                verifiedPackages,
                trustedBuildInfrastructureRoots);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAmbientReferenceResolutionItem(string itemName)
        => itemName.Equals("COMFileReference", StringComparison.OrdinalIgnoreCase) ||
           itemName.Equals("COMReference", StringComparison.OrdinalIgnoreCase) ||
           itemName.Equals("NativeReference", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateGeneratedConfigurationInputs(DotNetPublishPlan? plan)
    {
        if (plan is null)
            return;

        string[] generatedPaths = (plan.GeneratedConfigurationInputPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        if (generatedPaths.Length == 0)
            return;

        IReadOnlyDictionary<string, string> admitted = plan.GeneratedConfigurationInputSha256;
        foreach (string path in generatedPaths)
        {
            if (!admitted.TryGetValue(path, out string? expectedSha256) ||
                string.IsNullOrWhiteSpace(expectedSha256))
            {
                throw new InvalidOperationException(
                    $"Generated configuration evidence '{path}' has no admitted SHA-256 digest.");
            }
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Generated configuration evidence is missing: {path}");
            }

            byte[] admittedBytes = File.ReadAllBytes(path);
            string actualSha256 = ComputeSha256Hex(admittedBytes);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Generated configuration evidence changed after admission: {path}");
            }

            using var stream = new MemoryStream(admittedBytes, writable: false);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            PowerForgeReleaseConfigurationSecretValidator.ValidateJson(reader.ReadToEnd());
        }
    }

    internal static (int ExitCode, string StdOut, string StdErr, bool TimedOut) RunBuildInputEvaluationProcess(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        TimeSpan timeout,
        IReadOnlyList<KeyValuePair<string, string>>? controlledGitConfiguration = null,
        string? controlledGitIndexFile = null)
    {
        string effectiveFileName = fileName;
        IReadOnlyList<string> effectiveArguments = arguments;
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(ActiveDotNetExecutablePath.Value))
            {
                effectiveFileName = ResolveDotNetChildExecutable("dotnet");
            }
            else if (fileName.Equals("git", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(ActiveGitExecutablePath.Value))
            {
                effectiveFileName = ResolveGitChildExecutable("git");
            }
            else if (!TryResolveTrustedBuildTool(fileName, out effectiveFileName))
                return (-1, string.Empty, "Trusted build tool could not be resolved.", false);
        }
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                environmentVariables = CreateSafeDotNetChildEnvironment(environmentVariables);
            }
            catch (Exception exception)
            {
                return (-1, string.Empty, exception.GetBaseException().Message, false);
            }
        }
        if (fileName.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            effectiveArguments = new[] { "--no-replace-objects" }
                .Concat(arguments)
                .ToArray();
            environmentVariables = CreateTrustedGitEnvironment(
                environmentVariables,
                controlledGitConfiguration,
                controlledGitIndexFile);
        }
        return RunProcessCore(
            effectiveFileName,
            workingDirectory,
            effectiveArguments,
            timeout,
            environmentVariables);
    }

}
