using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellAnalyzeUsage =
        "Usage: powerforge powershell analyze <path> [--target-contract <target.json>] [--semantic-profile <id>] [--kind <exe|dll|library>] [--mode <Analyze|Package|Hybrid|Strict>] [--framework <tfm>] [--out <directory>] [--resource-mode <Declared|CompleteModule|None>] [--include-resource <path-or-glob> ...] [--exclude-resource <path-or-glob> ...] [--output json]";
    private const string PowerShellBuildUsage =
        "Usage: powerforge powershell build <path> [--path <additional.ps1> ...] [--entry-point <main.ps1>] [--kind <exe|dll|library>] [--out <directory>] [--name <artifact>] [--mode <Package|Hybrid|Strict>] [--target-contract <target.json>] [--semantic-profile <id>] [--framework <tfm>] [--dependency-lock <graph.json> | --allow-unreviewed-dependencies] [--expected-abi-sha256 <sha256>] [--resource-mode <Declared|CompleteModule|None>] [--include-resource <path-or-glob> ...] [--exclude-resource <path-or-glob> ...] [--rid <rid>] [--self-contained] [--optimization <None|Trimmed|NativeAot>] [--cache-directory <path>] [--no-build-cache] [--emit-source] [--emit-ir] [--sign] [--certificate-thumbprint <thumbprint>] [--certificate-store <CurrentUser|LocalMachine>] [--timestamp-server <url>] [--signing-timeout <seconds>] [--no-single-file] [--keep-workspace] [--output json]";
    private const string PowerShellCensusUsage =
        "Usage: powerforge powershell census <path> [--path <product-root> ...] [--framework <tfm>] [--baseline <census.json>] [--write-baseline <census.json>] [--no-recurse] [--output json]";
    private const string PowerShellExplainUsage =
        "Usage: powerforge powershell explain <path> [--target-contract <target.json>] [--semantic-profile <id>] [--kind <exe|dll|library>] [--mode <Analyze|Package|Hybrid|Strict>] [--framework <tfm>] [--out <directory>] [--resource-mode <Declared|CompleteModule|None>] [--include-resource <path-or-glob> ...] [--exclude-resource <path-or-glob> ...] [--output json]";
    private const string PowerShellDiagnoseUsage =
        "Usage: powerforge powershell diagnose <manifest.json> --failure <log.txt> [--output json]";

    private static int CommandPowerShell(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || argv.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!argv[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
        {
            if (argv[0].Equals("project", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellProject(argv.Skip(1).ToArray(), outputJson, logger);
            if (argv[0].Equals("support", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellSupport(argv.Skip(1).ToArray(), outputJson, logger);
            if (argv[0].Equals("build", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("compile", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellBuild(argv.Skip(1).ToArray(), outputJson, logger);
            if (argv[0].Equals("census", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("matrix", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellCensus(argv.Skip(1).ToArray(), outputJson, logger);
            if (argv[0].Equals("explain", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellExplain(argv.Skip(1).ToArray(), outputJson, logger);
            if (argv[0].Equals("diagnose", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellDiagnose(argv.Skip(1).ToArray(), outputJson, logger);
            return WritePowerShellError(outputJson, 2, $"Unknown PowerShell subcommand '{argv[0]}'.", logger);
        }

        return CommandPowerShellAnalyze(argv.Skip(1).ToArray(), outputJson, logger);
    }

    private static int CommandPowerShellBuild(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--path", "--entry-point", "--kind", "--target", "--out", "--output-directory", "--name", "--mode", "--target-contract", "--semantic-profile", "--framework", "--dependency-lock", "--expected-abi-sha256", "--resource-mode", "--include-resource", "--exclude-resource", "--rid", "--optimization", "--cache-directory", "--certificate-thumbprint", "--certificate-store", "--timestamp-server", "--signing-timeout", "--timeout", "--output" },
                new[] { "--self-contained", "--allow-unreviewed-dependencies", "--no-build-cache", "--emit-source", "--emit-ir", "--sign", "--no-single-file", "--keep-workspace", "--json", "--output-json" },
                out var positionalPath,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.build");

        var paths = GetOptionValues(args, "--path").ToList();
        if (positionalPath is not null)
            paths.Insert(0, positionalPath);
        if (paths.Count == 0)
            return WritePowerShellError(outputJson, 2, "A PowerShell source file is required.", logger, "powershell.build");

        var kindValue = TryGetOptionValue(args, "--kind") ?? TryGetOptionValue(args, "--target");
        PowerShellCompilationArtifactKind? kindOverride = null;
        if (!string.IsNullOrWhiteSpace(kindValue))
        {
            if (!TryParseArtifactKind(kindValue, out var parsedKind))
                return WritePowerShellError(outputJson, 2, "Artifact kind must be 'exe', 'dll', or 'library'.", logger, "powershell.build");
            kindOverride = parsedKind;
        }

        var modeValue = TryGetOptionValue(args, "--mode");
        PowerShellCompilationMode? modeOverride = null;
        if (!string.IsNullOrWhiteSpace(modeValue))
        {
            if (!Enum.TryParse<PowerShellCompilationMode>(modeValue, ignoreCase: true, out var parsedMode) ||
                !Enum.IsDefined(typeof(PowerShellCompilationMode), parsedMode) ||
                parsedMode == PowerShellCompilationMode.Analyze)
                return WritePowerShellError(outputJson, 2, $"Unknown artifact compilation mode '{modeValue}'.", logger, "powershell.build");
            modeOverride = parsedMode;
        }
        var resourceModeValue = TryGetOptionValue(args, "--resource-mode") ?? nameof(PowerShellCompilationResourceMode.Declared);
        if (!Enum.TryParse<PowerShellCompilationResourceMode>(resourceModeValue, ignoreCase: true, out var resourceMode) ||
            !Enum.IsDefined(typeof(PowerShellCompilationResourceMode), resourceMode))
            return WritePowerShellError(outputJson, 2, $"Unknown resource mode '{resourceModeValue}'. Use Declared, CompleteModule, or None.", logger, "powershell.build");

        try
        {
            PowerShellCompilationTargetContract? targetContract = null;
            var targetFramework = TryGetOptionValue(args, "--framework") ?? "net8.0";
            var semanticProfileId = TryGetOptionValue(args, "--semantic-profile") ??
                                    PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(targetFramework);
            semanticProfileId = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).ProfileId;
            var targetContractPath = TryGetOptionValue(args, "--target-contract");
            if (!string.IsNullOrWhiteSpace(targetContractPath))
            {
                var fullTargetContractPath = Path.GetFullPath(targetContractPath.Trim().Trim('"'));
                targetContract = JsonSerializer.Deserialize<PowerShellCompilationTargetContract>(
                    File.ReadAllText(fullTargetContractPath),
                    CliJson.Options)
                    ?? throw new InvalidDataException($"Target contract '{fullTargetContractPath}' did not contain a contract.");
                targetContract = PowerShellCompilationTargetContractService.Normalize(targetContract);
                if (args.Any(argument => argument.Equals("--semantic-profile", StringComparison.OrdinalIgnoreCase)) &&
                    !semanticProfileId.Equals(targetContract.SemanticProfileId, StringComparison.Ordinal))
                    return WritePowerShellError(outputJson, 2, "The explicit semantic profile conflicts with the target contract.", logger, "powershell.build");
                semanticProfileId = targetContract.SemanticProfileId;
                if (kindOverride.HasValue && kindOverride.Value != targetContract.ArtifactKind)
                    return WritePowerShellError(outputJson, 2, "The explicit artifact kind conflicts with the target contract.", logger, "powershell.build");
                if (modeOverride.HasValue && modeOverride.Value != targetContract.Mode)
                    return WritePowerShellError(outputJson, 2, "The explicit compilation mode conflicts with the target contract.", logger, "powershell.build");
                kindOverride = targetContract.ArtifactKind;
                modeOverride = targetContract.Mode;
            }
            var fullPaths = paths.Select(path => Path.GetFullPath(path.Trim().Trim('"'))).ToArray();
            var entryPoint = TryGetOptionValue(args, "--entry-point");
            var fullEntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? null : Path.GetFullPath(entryPoint.Trim().Trim('"'));
            var resolved = new PowerShellCompilationInputResolver().Resolve(
                fullPaths,
                kindOverride,
                modeOverride,
                fullEntryPoint,
                allowDynamicModuleRuntimeSources: resourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                                  modeOverride != PowerShellCompilationMode.Strict);
            var outputDirectory = TryGetOptionValue(args, "--out") ?? TryGetOptionValue(args, "--output-directory") ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved);
            var artifactName = TryGetOptionValue(args, "--name") ?? resolved.ArtifactName;
            var optimizationValue = TryGetOptionValue(args, "--optimization") ?? nameof(PowerShellCompilationExecutableOptimization.None);
            if (!Enum.TryParse<PowerShellCompilationExecutableOptimization>(optimizationValue, ignoreCase: true, out var optimization) ||
                !Enum.IsDefined(typeof(PowerShellCompilationExecutableOptimization), optimization))
                return WritePowerShellError(outputJson, 2, $"Unknown executable optimization '{optimizationValue}'. Use None, Trimmed, or NativeAot.", logger, "powershell.build");
            var certificateStoreValue = TryGetOptionValue(args, "--certificate-store") ?? nameof(CertificateStoreLocation.CurrentUser);
            if (!Enum.TryParse<CertificateStoreLocation>(certificateStoreValue, ignoreCase: true, out var certificateStore) ||
                !Enum.IsDefined(typeof(CertificateStoreLocation), certificateStore))
                return WritePowerShellError(outputJson, 2, $"Unknown certificate store '{certificateStoreValue}'. Use CurrentUser or LocalMachine.", logger, "powershell.build");
            PowerShellCompilationDependencyGraph? expectedDependencyLock = null;
            var dependencyLockPath = TryGetOptionValue(args, "--dependency-lock");
            if (!string.IsNullOrWhiteSpace(dependencyLockPath))
            {
                var fullDependencyLockPath = Path.GetFullPath(dependencyLockPath.Trim().Trim('"'));
                expectedDependencyLock = JsonSerializer.Deserialize<PowerShellCompilationDependencyGraph>(
                    File.ReadAllText(fullDependencyLockPath),
                    CliJson.Options)
                    ?? throw new InvalidDataException($"Dependency lock '{fullDependencyLockPath}' did not contain a graph.");
            }
            var spec = new PowerShellCompilationBuildSpec(resolved.SourcePath, outputDirectory, artifactName, resolved.Kind, resolved.Mode)
            {
                ModuleManifestPath = resolved.ModuleManifestPath,
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                ResourceMode = resourceMode,
                IncludeResource = GetOptionValues(args, "--include-resource").ToArray(),
                ExcludeResource = GetOptionValues(args, "--exclude-resource").ToArray(),
                TargetFramework = targetFramework,
                RuntimeIdentifier = TryGetOptionValue(args, "--rid"),
                SelfContained = args.Any(static argument => argument.Equals("--self-contained", StringComparison.OrdinalIgnoreCase)),
                SingleFile = !args.Any(static argument => argument.Equals("--no-single-file", StringComparison.OrdinalIgnoreCase)),
                Optimization = optimization,
                TargetContract = targetContract,
                SemanticProfileId = semanticProfileId,
                UseBuildCache = !args.Any(static argument => argument.Equals("--no-build-cache", StringComparison.OrdinalIgnoreCase)),
                BuildCacheDirectory = TryGetOptionValue(args, "--cache-directory"),
                SignArtifact = args.Any(static argument => argument.Equals("--sign", StringComparison.OrdinalIgnoreCase)),
                CertificateThumbprint = TryGetOptionValue(args, "--certificate-thumbprint"),
                CertificateStoreLocation = certificateStore,
                TimeStampServer = TryGetOptionValue(args, "--timestamp-server") ?? "http://timestamp.digicert.com",
                KeepBuildWorkspace = args.Any(static argument => argument.Equals("--keep-workspace", StringComparison.OrdinalIgnoreCase)),
                EmitSource = args.Any(static argument => argument.Equals("--emit-source", StringComparison.OrdinalIgnoreCase)),
                EmitIrSnapshots = args.Any(static argument => argument.Equals("--emit-ir", StringComparison.OrdinalIgnoreCase)),
                ExpectedPublicAbiSha256 = TryGetOptionValue(args, "--expected-abi-sha256"),
                ExpectedDependencyLock = expectedDependencyLock,
                AllowUnreviewedDependencyResolution = args.Any(static argument => argument.Equals("--allow-unreviewed-dependencies", StringComparison.OrdinalIgnoreCase))
            };
            var signingTimeoutValue = TryGetOptionValue(args, "--signing-timeout");
            if (!string.IsNullOrWhiteSpace(signingTimeoutValue))
            {
                if (!int.TryParse(signingTimeoutValue, out var signingTimeoutSeconds) || signingTimeoutSeconds < 1)
                    return WritePowerShellError(outputJson, 2, "--signing-timeout must be a positive number of seconds.", logger, "powershell.build");
                spec.SigningTimeoutSeconds = signingTimeoutSeconds;
            }
            var timeoutValue = TryGetOptionValue(args, "--timeout");
            if (!string.IsNullOrWhiteSpace(timeoutValue))
            {
                if (!int.TryParse(timeoutValue, out var timeoutSeconds) || timeoutSeconds < 1)
                    return WritePowerShellError(outputJson, 2, "--timeout must be a positive number of seconds.", logger, "powershell.build");
                spec.TimeoutSeconds = timeoutSeconds;
            }

            var result = new PowerShellCompilationArtifactBuilder().Build(spec);
            var exitCode = result.Succeeded ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.build",
                    Success = result.Succeeded,
                    ExitCode = exitCode,
                    Error = result.Error,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationBuildResult)
                });
                return exitCode;
            }

            if (!result.Succeeded)
            {
                logger.Error(result.Error ?? "PowerShell artifact build failed.");
                if (!string.IsNullOrWhiteSpace(result.BuildOutput)) logger.Error(result.BuildOutput);
                return exitCode;
            }

            logger.Success($"Built {resolved.Kind}: {result.ArtifactPath}");
            logger.Info($"Manifest: {result.ManifestPath}");
            if (!string.IsNullOrWhiteSpace(result.GeneratedSourcePath))
                logger.Info($"Generated source: {result.GeneratedSourcePath}");
            logger.Info(result.Manifest!.UsesPowerShellRuntimeFallback
                ? $"Runtime-packaged artifact: {result.Manifest.RuntimeFallbackUnits} unit(s) execute through PowerShell; {result.Manifest.PromotedTypedRegions} typed region(s) run in generated CLR helpers."
                : result.Manifest.RequiresPowerShellRuntime
                    ? $"Typed PowerShell binary module: {result.Manifest.CompiledMethods} cmdlet(s), no dynamic script fallback."
                    : $"Typed CLR artifact: {result.Manifest.CompiledMethods} method(s), {result.Manifest.OmittedUnits} unsupported unit(s) omitted, no PowerShell runtime dependency.");
            return 0;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger, "powershell.build");
        }
    }

    private static int CommandPowerShellAnalyze(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--path", "--target-contract", "--semantic-profile", "--kind", "--mode", "--framework", "--out", "--output-directory", "--resource-mode", "--include-resource", "--exclude-resource", "--output" },
                new[] { "--json", "--output-json" },
                out var positionalPath,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger);

        if (!TryParsePowerShellAnalysisRequest(args, positionalPath, out var request, out var requestError))
            return WritePowerShellError(outputJson, 2, requestError, logger, "powershell.analyze");

        try
        {
            var plan = CreatePowerShellAnalysisPlan(args, request!);
            var exitCode = plan.CanProceed ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.analyze",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(plan, CliJson.Context.PowerShellCompilationPlan)
                });
                return exitCode;
            }

            WritePowerShellPlan(plan, logger);
            return exitCode;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger);
        }
    }

    private static int CommandPowerShellCensus(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--path", "--framework", "--baseline", "--write-baseline", "--output" },
                new[] { "--no-recurse", "--json", "--output-json" },
                out var positionalPath,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.census");

        var paths = GetOptionValues(args, "--path").ToList();
        if (positionalPath is not null)
            paths.Insert(0, positionalPath);
        if (paths.Count == 0)
            return WritePowerShellError(outputJson, 2, "At least one PowerShell product or source path is required.", logger, "powershell.census");

        try
        {
            var baselinePath = TryGetOptionValue(args, "--baseline");
            PowerShellCompilationCensusResult? baseline = null;
            if (!string.IsNullOrWhiteSpace(baselinePath))
            {
                var fullBaselinePath = Path.GetFullPath(baselinePath.Trim().Trim('"'));
                baseline = JsonSerializer.Deserialize(
                    File.ReadAllText(fullBaselinePath),
                    CliJson.Context.PowerShellCompilationCensusResult)
                    ?? throw new InvalidDataException($"Compilation census baseline is empty: {fullBaselinePath}");
            }

            var result = new PowerShellCompilationCensusRunner().Run(
                paths,
                TryGetOptionValue(args, "--framework"),
                baseline,
                recurse: !args.Any(static argument => argument.Equals("--no-recurse", StringComparison.OrdinalIgnoreCase)));
            var writeBaselinePath = TryGetOptionValue(args, "--write-baseline");
            if (!string.IsNullOrWhiteSpace(writeBaselinePath))
            {
                var fullWritePath = Path.GetFullPath(writeBaselinePath.Trim().Trim('"'));
                var parent = Path.GetDirectoryName(fullWritePath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(fullWritePath, JsonSerializer.Serialize(result, CliJson.Context.PowerShellCompilationCensusResult));
            }

            var exitCode = result.Passed ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.census",
                    Success = result.Passed,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationCensusResult)
                });
                return exitCode;
            }

            if (result.PostEmissionEvaluated)
                logger.Info($"PowerShell compilation census: {result.SourceFiles} files, {result.EmittedFunctions}/{result.TotalFunctions} functions emitted ({result.EmittedFunctionCoveragePercentage:0.0}%), {result.PromotedTypedRegions} typed region(s) promoted, {result.Products.Sum(static product => product.RejectedTypedRegions)} candidate region(s) retained, and {result.Products.Sum(static product => product.RegionOpportunities.Length)} analysis-only typed opportunity region(s), {result.DroppedEligibleFunctions} analyzer-eligible function(s) dropped after shaping, {result.ParseErrorFiles} parse-error files.");
            else
                logger.Info($"PowerShell compilation census: {result.SourceFiles} files, {result.CompilableUnits}/{result.TotalUnits} units structurally eligible; post-emission shaping was not evaluated, {result.ParseErrorFiles} parse-error files.");
            foreach (var product in result.Products)
            {
                if (product.Coverage.PostEmissionEvaluated)
                    logger.Info($"{product.Name}: {product.SourceFiles} files, {product.Coverage.EmittedFunctions}/{product.Coverage.TotalFunctions} functions emitted ({product.Coverage.EmittedFunctionCoveragePercentage:0.0}%), {product.PromotedTypedRegions} typed region(s) promoted, {product.RejectedTypedRegions} candidate region(s) retained, and {product.RegionOpportunities.Length} analysis-only typed opportunity region(s), {product.Coverage.TotalScriptUnits} script/init unit(s), {product.AnalysisMilliseconds:0.0} ms.");
                else
                    logger.Info($"{product.Name}: {product.SourceFiles} files, {product.CompilableUnits}/{product.TotalUnits} units structurally eligible; post-emission shaping was not evaluated, {product.AnalysisMilliseconds:0.0} ms.");
                foreach (var feature in product.FunctionImpacts.Take(5))
                    logger.Warn($"  [{feature.FeatureId}] {feature.AffectedUnits} affected, {feature.VisibleSoleBlockerUnits} visible sole-blocker candidate(s), candidate coverage {feature.CandidateCoveragePercentage:0.0}%.");
                foreach (var decision in product.RegionCandidates
                             .Where(static candidate => !candidate.Promoted)
                             .GroupBy(static candidate => candidate.DecisionCode, StringComparer.Ordinal)
                             .OrderByDescending(static group => group.Count())
                             .ThenBy(static group => group.Key, StringComparer.Ordinal)
                             .Take(3))
                    logger.Warn($"  [{decision.Key}] {decision.Count()} typed-region candidate(s) retained: {decision.First().Reason}");
                var payload = product.DependencySummary.Where(static summary =>
                    summary.Kind is not PowerShellCompilationDependencyKind.PowerShellSource and not PowerShellCompilationDependencyKind.ModuleManifest).ToArray();
                if (payload.Length > 0)
                    logger.Info($"  Dependencies: {payload.Sum(static summary => summary.Files)} item(s), {payload.Sum(static summary => summary.SizeBytes)} byte(s), {payload.Sum(static summary => summary.Missing)} missing.");
                var resources = product.ResourceSummary;
                if (resources.IncludedFiles + resources.ExcludedFiles + resources.UnclassifiedFiles > 0)
                    logger.Info($"  Resources: {resources.IncludedFiles} included, {resources.RequiredFiles} required, {resources.InferredFiles} inferred, {resources.ExcludedFiles} excluded, {resources.UnclassifiedFiles} unclassified ({resources.IncludedBytes + resources.ExcludedBytes + resources.UnclassifiedBytes} inventoried byte(s)).");
            }
            if (result.FunctionFrontier.Length > 0)
            {
                logger.Info("Observed emitted-function frontier (current visible blockers; not a guarantee that masked blockers will not appear):");
                var rank = 0;
                foreach (var feature in result.FunctionFrontier.Take(10))
                {
                    logger.Info($"  {++rank}. [{feature.FeatureId}] {feature.Title}: {feature.AffectedUnits} affected, {feature.VisibleSoleBlockerUnits} visible sole-blocker candidate(s), {feature.AffectedProducts} product(s), candidate coverage {feature.CandidateCoveragePercentage:0.0}%.");
                    logger.Info($"     {feature.Recommendation}");
                }
            }
            if (result.FunctionCoBlockers.Length > 0)
            {
                logger.Info("Frequent function co-blockers:");
                foreach (var pair in result.FunctionCoBlockers.Take(5))
                    logger.Info($"  [{pair.FirstFeatureId}] + [{pair.SecondFeatureId}]: {pair.AffectedUnits} unit(s).");
            }
            foreach (var drift in result.SourceDrifts)
                logger.Error($"{drift.Product}: census source content differs from the baseline fingerprint.");
            foreach (var regression in result.Regressions)
                logger.Error($"Census regression in {regression.Product}: {regression.Metric} was {regression.Baseline:0.###}, now {regression.Current:0.###}.");
            return exitCode;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger, "powershell.census");
        }
    }

    private static void WritePowerShellPlan(PowerShellCompilationPlan plan, ILogger logger)
    {
        logger.Info($"PowerShell compilation plan ({plan.Mode}): {plan.CompilableUnits}/{plan.TotalUnits} units eligible ({plan.CompilationCoveragePercentage:0.0}%).");
        foreach (var file in plan.Files)
        {
            foreach (var diagnostic in file.Diagnostics)
                logger.Error(FormatPowerShellDiagnostic(file.RelativePath, diagnostic));

            foreach (var unit in file.Units)
            {
                var location = $"{file.RelativePath}:{unit.StartLine}";
                if (unit.IsCompilable)
                {
                    logger.Success($"COMPILE  {location}  {unit.Name}");
                    continue;
                }

                logger.Warn($"FALLBACK {location}  {unit.Name}");
                foreach (var diagnostic in unit.Diagnostics)
                    logger.Warn($"  {FormatPowerShellDiagnostic(file.RelativePath, diagnostic)}");
            }
        }

        if (plan.Dependencies.Length > 0)
        {
            logger.Info($"Dependency/resource plan: {plan.Dependencies.Length} item(s), {plan.Dependencies.Sum(static dependency => dependency.SizeBytes)} byte(s).");
            foreach (var group in plan.Dependencies
                         .GroupBy(static dependency => new { dependency.Kind, dependency.Disposition })
                         .OrderBy(static group => group.Key.Kind)
                         .ThenBy(static group => group.Key.Disposition))
                logger.Info($"  {group.Key.Kind} / {group.Key.Disposition}: {group.Count()} item(s), {group.Sum(static dependency => dependency.SizeBytes)} byte(s).");
            foreach (var dependency in plan.Dependencies.Where(static dependency =>
                         dependency.Disposition is PowerShellCompilationDependencyDisposition.NotIncluded or PowerShellCompilationDependencyDisposition.Missing).Take(20))
                logger.Warn($"  {dependency.RelativePath}: {dependency.Disposition}. {dependency.Note}");
            var resources = plan.ResourceSummary;
            logger.Info($"Resource selection: included {resources.IncludedFiles} file(s) / {resources.IncludedBytes} byte(s); required {resources.RequiredFiles} / {resources.RequiredBytes}; inferred {resources.InferredFiles} / {resources.InferredBytes}; excluded {resources.ExcludedFiles} / {resources.ExcludedBytes}; unclassified {resources.UnclassifiedFiles} / {resources.UnclassifiedBytes}.");
        }

        if (!plan.CanProceed)
            logger.Error(plan.ParseErrorFiles > 0
                ? $"Planning failed because {plan.ParseErrorFiles} file(s) contain parser errors."
                : $"Strict mode rejected {plan.RuntimeFallbackUnits} runtime fallback unit(s).");
    }

    private static string FormatPowerShellDiagnostic(string relativePath, PowerShellCompilationDiagnostic diagnostic)
        => $"{relativePath}:{diagnostic.Line}:{diagnostic.Column} [{diagnostic.Code}/{diagnostic.FeatureId}] {diagnostic.Message}";

    private static int WritePowerShellError(bool outputJson, int exitCode, string error, ILogger logger, string command = "powershell.analyze")
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = exitCode,
                Error = error
            });
        }
        else
        {
            logger.Error(error);
            if (exitCode == 2)
            {
                Console.WriteLine(PowerShellAnalyzeUsage);
                Console.WriteLine(PowerShellBuildUsage);
                Console.WriteLine(PowerShellCensusUsage);
            }
        }

        return exitCode;
    }

    private static void WritePowerShellHelp(bool outputJson)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "powershell",
                Success = true,
                ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(new { analyzeUsage = PowerShellAnalyzeUsage, explainUsage = PowerShellExplainUsage, diagnoseUsage = PowerShellDiagnoseUsage, buildUsage = PowerShellBuildUsage, censusUsage = PowerShellCensusUsage, projectUsage = PowerShellProjectUsage, supportUsage = PowerShellSupportUsage })
            });
        }
        else
        {
            Console.WriteLine(PowerShellAnalyzeUsage);
            Console.WriteLine(PowerShellExplainUsage);
            Console.WriteLine(PowerShellBuildUsage);
            Console.WriteLine(PowerShellCensusUsage);
            Console.WriteLine(PowerShellDiagnoseUsage);
            Console.WriteLine(PowerShellProjectUsage);
            Console.WriteLine(PowerShellSupportUsage);
        }
    }

    private static bool TryParseArtifactKind(string? value, out PowerShellCompilationArtifactKind kind)
    {
        if (value is not null && (value.Equals("exe", StringComparison.OrdinalIgnoreCase) || value.Equals("executable", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.Executable;
            return true;
        }
        if (value is not null && (value.Equals("dll", StringComparison.OrdinalIgnoreCase) || value.Equals("module", StringComparison.OrdinalIgnoreCase) || value.Equals("binarymodule", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.BinaryModule;
            return true;
        }
        if (value is not null && (value.Equals("library", StringComparison.OrdinalIgnoreCase) || value.Equals("clr", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.Library;
            return true;
        }
        kind = default;
        return false;
    }

    private static bool TryValidatePowerShellArguments(
        string[] args,
        IEnumerable<string> valueOptions,
        IEnumerable<string> switchOptions,
        out string? positionalArgument,
        out string error)
    {
        var values = valueOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var switches = switchOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        positionalArgument = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (switches.Contains(argument)) continue;
            if (values.Contains(argument))
            {
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"PowerShell option '{argument}' requires a value.";
                    return false;
                }
                continue;
            }
            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Unknown PowerShell option '{argument}'.";
                return false;
            }
            if (positionalArgument is not null)
            {
                error = $"Unexpected PowerShell argument '{argument}'.";
                return false;
            }
            positionalArgument = argument;
        }

        error = string.Empty;
        return true;
    }

    private static IEnumerable<string> GetOptionValues(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            yield return args[++index];
        }
    }

    private static bool IsHelpArgument(string argument)
        => argument.Equals("-h", StringComparison.OrdinalIgnoreCase) || argument.Equals("--help", StringComparison.OrdinalIgnoreCase);
}
