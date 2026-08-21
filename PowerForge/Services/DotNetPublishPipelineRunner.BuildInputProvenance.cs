using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly string[] EvaluatedBuildItemNames =
    [
        "Compile",
        "Content",
        "EmbeddedResource",
        "AdditionalFiles",
        "Analyzer",
        "Reference",
        "ReferencePath",
        "ReferenceCopyLocalPaths",
        "EditorConfigFiles",
        "GlobalAnalyzerConfigFiles",
        "ApplicationDefinition",
        "Page",
        "Resource",
        "SplashScreen",
        "RazorComponent",
        "TypeScriptCompile",
        "None",
        "ProjectReference"
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
        out HashSet<string> sourceInputs)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ProjectEvaluationRequest[] roots = BuildProjectEvaluationRequests(
                projectPaths,
                configuration,
                buildPlan)
            .ToArray();
        var visited = new HashSet<string>(comparison);
        var directories = new HashSet<string>(comparison);
        using var verifiedPackageArchives = new VerifiedPackageArchiveCache();
        buildInputs = new HashSet<string>(comparison);
        sourceInputs = new HashSet<string>(comparison);

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
                if (request.TargetFramework is null)
                {
                    if (evaluation.TargetFrameworks.Length > 0)
                    {
                        foreach (string targetFramework in evaluation.TargetFrameworks)
                            pending.Enqueue(request.ForProject(request.ProjectPath, targetFramework));
                    }
                    else
                    {
                        foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                            pending.Enqueue(request.ForProject(projectReference.ProjectPath, targetFramework: null));
                    }
                }
                else
                {
                    foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                        pending.Enqueue(request.ForProject(projectReference.ProjectPath, projectReference.TargetFramework));
                }
            }
        }

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
            "-getProperty:BaseIntermediateOutputPath",
            "-getProperty:MSBuildProjectExtensionsPath",
            "-getProperty:IntermediateOutputPath",
            "-getProperty:NuGetPackageRoot",
            "-getProperty:NuGetPackageFolders",
            "-getProperty:ProjectAssetsFile",
            "-getProperty:NuGetLockFilePath",
            "-getProperty:MSBuildToolsPath",
            "-p:Configuration=" + request.Configuration
        };
        foreach (string itemName in EvaluatedBuildItemNames)
            arguments.Add("-getItem:" + itemName);
        if (!string.IsNullOrWhiteSpace(request.TargetFramework))
        {
            arguments.Add("-target:ResolveReferences");
            arguments.Add("-getItem:_MSBuildProjectReferenceExistent");
            arguments.Add("-p:TargetFramework=" + request.TargetFramework);
        }
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + property.Value);
        }
        arguments.Add("-p:BuildProjectReferences=false");

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
                return false;

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            JsonElement root = document.RootElement;
            var inputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var sourceInputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var references = new Dictionary<string, EvaluatedProjectReference>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var rawReferences = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedBuildRoots = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var packageRoots = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                AddPropertyPath(properties, "BaseOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "OutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "BaseIntermediateOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "MSBuildProjectExtensionsPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
                AddPropertyPath(properties, "IntermediateOutputPath", Path.GetDirectoryName(request.ProjectPath)!, generatedBuildRoots);
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

                VerifiedPackageInputCatalog? verifiedPackages =
                    VerifiedPackageInputCatalog.TryCreate(
                        request.ProjectPath,
                        properties,
                        packageRoots,
                        verifiedPackageArchives);
                string[] trustedBuildInfrastructureRoots =
                    ReadTrustedBuildInfrastructureRoots(properties, Path.GetDirectoryName(request.ProjectPath)!);
                var importPaths = new HashSet<string>(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                AddSemicolonSeparatedPathValues(
                    properties,
                    "MSBuildAllProjects",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    importPaths);
                if (!TryReadPreprocessedProjectImports(request, out string[] preprocessedImports))
                    return false;
                importPaths.UnionWith(preprocessedImports);
                importPaths.UnionWith(ReadDeclaredBuildInputCandidates(
                    request.ProjectPath,
                    importPaths));
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
                    string? msBuildToolsPath = ReadItemText(properties, "MSBuildToolsPath");
                    foreach (string itemName in EvaluatedBuildItemNames)
                    {
                        if (!items.TryGetProperty(itemName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                            continue;
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
                            if (IsBelowGeneratedBuildRoot(fullPath, generatedBuildRoots))
                                continue;
                            if (itemName.Equals("ProjectReference", StringComparison.Ordinal))
                            {
                                inputs.Add(fullPath);
                                rawReferences.Add(fullPath);
                            }
                            else if (IsGeneratedProjectReferenceOutput(
                                         itemName,
                                         fullPath,
                                         item,
                                         msBuildToolsPath,
                                         embeddedResourceProjectReferences,
                                         analyzerProjectReferences))
                            {
                                // The referenced project's evaluated sources are queued below; its compiled
                                // output (including analyzer/source-generator outputs) is generated state and
                                // cannot become a release source input.
                                continue;
                            }
                            else if (!itemName.Equals("None", StringComparison.Ordinal) || IsOutputRelevantNoneItem(item))
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
                foreach (string projectReference in rawReferences)
                    references[projectReference] = new EvaluatedProjectReference(projectReference, targetFramework: null);
            }
            else if (rawReferences.Count > 0)
            {
                if (!root.TryGetProperty("Items", out JsonElement resolvedItems) ||
                    !TryReadResolvedProjectReferences(
                        resolvedItems,
                        out EvaluatedProjectReference[] resolvedReferences))
                    return false;
                foreach (EvaluatedProjectReference reference in resolvedReferences)
                {
                    references[reference.ProjectPath] = reference;
                    inputs.Add(reference.ProjectPath);
                }
            }

            evaluation = new EvaluatedProjectInputs(
                inputs.ToArray(),
                sourceInputs.ToArray(),
                references.Values.ToArray(),
                targetFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveEvaluatedItemPath(
        JsonElement item,
        string metadataName,
        string baseDirectory,
        out string? fullPath)
    {
        fullPath = null;
        string? value = ReadItemText(item, metadataName);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            fullPath = Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(baseDirectory, value));
            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

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
        TimeSpan timeout)
        => RunProcessCore(
            fileName,
            workingDirectory,
            arguments,
            timeout,
            environmentVariables);

    private static bool IsOutputRelevantNoneItem(JsonElement item)
        => HasRelevantMetadata(item, "CopyToOutputDirectory")
           || HasRelevantMetadata(item, "CopyToPublishDirectory")
           || (item.TryGetProperty("Pack", out JsonElement pack) &&
               pack.ValueKind == JsonValueKind.String &&
               bool.TryParse(pack.GetString(), out bool packs) && packs);

    private static bool HasRelevantMetadata(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
           && !value.GetString()!.Equals("Never", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadEvaluatedProjectReference(
        JsonElement item,
        out EvaluatedProjectReference? reference)
    {
        reference = null;
        if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
            fullPathElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(fullPathElement.GetString()))
        {
            return false;
        }

        string? targetFramework = ReadItemText(item, "NearestTargetFramework");
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            string? setTargetFramework = ReadItemText(item, "SetTargetFramework");
            const string prefix = "TargetFramework=";
            if (!string.IsNullOrWhiteSpace(setTargetFramework) &&
                setTargetFramework!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                targetFramework = setTargetFramework.Substring(prefix.Length).Trim();
            }
        }

        reference = new EvaluatedProjectReference(
            Path.GetFullPath(fullPathElement.GetString()!),
            string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework);
        return true;
    }

    private static bool TryReadResolvedProjectReferences(
        JsonElement items,
        out EvaluatedProjectReference[] references)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        if (!items.TryGetProperty(
                "_MSBuildProjectReferenceExistent",
                out JsonElement resolvedReferences) ||
            resolvedReferences.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        references = resolvedReferences.EnumerateArray()
            .Select(item => TryReadEvaluatedProjectReference(item, out EvaluatedProjectReference? reference)
                ? reference
                : null)
            .Where(static reference => reference is not null)
            .Cast<EvaluatedProjectReference>()
            .ToArray();
        // An empty resolved item list is a valid result for a conditional
        // ProjectReference that does not participate in this target framework.
        return true;
    }

    private static string? ReadItemText(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void AddSemicolonSeparatedPathValues(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                values.Add(fullPath);
        }
    }

    private static void AddSemicolonSeparatedValues(
        JsonElement properties,
        string name,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(value.Trim());
        }
    }

}
