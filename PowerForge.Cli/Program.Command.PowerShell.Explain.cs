using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandPowerShellExplain(string[] args, bool outputJson, ILogger logger)
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
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.explain");
        if (!TryParsePowerShellAnalysisRequest(args, positionalPath, out var request, out var requestError))
            return WritePowerShellError(outputJson, 2, requestError, logger, "powershell.explain");

        try
        {
            var explanation = CreatePowerShellExplanation(args, request!);
            var exitCode = explanation.CanProceed ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.explain",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(explanation, CliJson.Context.PowerShellCompilationExplanation)
                });
                return exitCode;
            }

            logger.Info($"PowerShell compilation decisions ({explanation.Mode}): {explanation.TypedUnits} typed, {explanation.RuntimeFallbackUnits} runtime fallback, {explanation.RejectedUnits} rejected.");
            foreach (var file in explanation.Files)
            {
            foreach (var cause in file.Causes)
                logger.Error($"FILE            {file.RelativePath}:{cause.Line}:{cause.Column}  {cause.Code} / {cause.FeatureId}: {cause.Message}");
            foreach (var unit in file.Units)
            {
                var location = $"{file.RelativePath}:{unit.StartLine}";
                var line = $"{unit.Decision.ToString().ToUpperInvariant(),-15} {location}  {unit.Name} [{unit.UnitId}]";
                if (unit.Decision == PowerShellCompilationDecisionKind.Typed) logger.Success(line);
                else if (unit.Decision == PowerShellCompilationDecisionKind.Rejected) logger.Error(line);
                else logger.Warn(line);
                if (unit.RegionGraph is not null)
                {
                    foreach (var region in unit.RegionGraph.Regions)
                        logger.Info($"  REGION {region.Ordinal} {region.Execution}: {region.Inputs.Count} inputs, {region.Outputs.Count} outputs, {region.Mutations.Count} mutations, {region.StaticBoundaryCrossings} crossings, cost {region.StaticBoundaryCostUnits}");
                }
                foreach (var cause in unit.Causes)
                    logger.Warn($"  {cause.Code} / {cause.FeatureId} at {cause.Line}:{cause.Column}: {cause.Message}");
            }
            }
            foreach (var dependency in explanation.DependencyCauses)
                logger.Error($"DEPENDENCY      {dependency.RelativePath}  {dependency.Kind} / {dependency.Discovery}: {dependency.Message}");
            return exitCode;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger, "powershell.explain");
        }
    }

    private static bool TryParsePowerShellAnalysisRequest(
        string[] args,
        string? positionalPath,
        out PowerShellAnalysisRequest? request,
        out string error)
    {
        request = null;
        var path = TryGetOptionValue(args, "--path");
        if (!string.IsNullOrWhiteSpace(path) && positionalPath is not null)
        {
            error = "Specify the PowerShell analysis path either positionally or with --path, not both.";
            return false;
        }
        path = string.IsNullOrWhiteSpace(path) ? positionalPath : path;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "A PowerShell file or directory path is required.";
            return false;
        }
        PowerShellCompilationTargetContract? target = null;
        var requestedFramework = TryGetOptionValue(args, "--framework") ?? "net8.0";
        var semanticProfileId = TryGetOptionValue(args, "--semantic-profile") ??
                                PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(requestedFramework);
        try
        {
            semanticProfileId = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).ProfileId;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        var targetPath = TryGetOptionValue(args, "--target-contract");
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            try
            {
                var fullTargetPath = Path.GetFullPath(targetPath.Trim().Trim('"'));
                target = JsonSerializer.Deserialize<PowerShellCompilationTargetContract>(File.ReadAllText(fullTargetPath), CliJson.Options)
                         ?? throw new InvalidDataException($"Target contract '{fullTargetPath}' was empty.");
                target = PowerShellCompilationTargetContractService.Normalize(target);
                if (args.Any(argument => argument.Equals("--semantic-profile", StringComparison.OrdinalIgnoreCase)) &&
                    !semanticProfileId.Equals(target.SemanticProfileId, StringComparison.Ordinal))
                    throw new InvalidDataException("The explicit semantic profile conflicts with the target contract.");
                semanticProfileId = target.SemanticProfileId;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        PowerShellCompilationArtifactKind? kind = target?.ArtifactKind;
        var kindValue = TryGetOptionValue(args, "--kind");
        if (!string.IsNullOrWhiteSpace(kindValue))
        {
            if (!TryParseArtifactKind(kindValue, out var parsedKind))
            {
                error = "Artifact kind must be 'exe', 'dll', or 'library'.";
                return false;
            }
            kind = parsedKind;
        }
        if (target is not null && kind != target.ArtifactKind)
        {
            error = "The explicit artifact kind conflicts with the target contract.";
            return false;
        }
        var modeValue = TryGetOptionValue(args, "--mode") ?? target?.Mode.ToString() ?? nameof(PowerShellCompilationMode.Analyze);
        if (!Enum.TryParse<PowerShellCompilationMode>(modeValue, true, out var mode) || !Enum.IsDefined(typeof(PowerShellCompilationMode), mode))
        {
            error = $"Unknown compilation mode '{modeValue}'.";
            return false;
        }
        if (target is not null && mode != target.Mode)
        {
            error = "The explicit compilation mode conflicts with the target contract.";
            return false;
        }
        var frameworkValue = TryGetOptionValue(args, "--framework");
        if (target is not null && !string.IsNullOrWhiteSpace(frameworkValue) &&
            !frameworkValue.Equals(target.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            error = "The explicit target framework conflicts with the target contract.";
            return false;
        }
        var resourceValue = TryGetOptionValue(args, "--resource-mode") ?? nameof(PowerShellCompilationResourceMode.Declared);
        if (!Enum.TryParse<PowerShellCompilationResourceMode>(resourceValue, true, out var resourceMode) || !Enum.IsDefined(typeof(PowerShellCompilationResourceMode), resourceMode))
        {
            error = $"Unknown resource mode '{resourceValue}'. Use Declared, CompleteModule, or None.";
            return false;
        }
        request = new PowerShellAnalysisRequest(path, kind, mode, resourceMode, semanticProfileId, target);
        error = string.Empty;
        return true;
    }

    private static PowerShellCompilationPlan CreatePowerShellAnalysisPlan(string[] args, PowerShellAnalysisRequest request)
    {
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            request.Path,
            request.Kind,
            request.Mode == PowerShellCompilationMode.Analyze ? null : request.Mode,
            allowDynamicModuleRuntimeSources: request.ResourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                              request.Mode != PowerShellCompilationMode.Strict);
        return new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(), request.SemanticProfileId).Analyze(
            resolved,
            request.Mode,
            request.TargetContract?.TargetFramework ?? TryGetOptionValue(args, "--framework") ?? "net8.0",
            request.ResourceMode,
            GetOptionValues(args, "--include-resource"),
            GetOptionValues(args, "--exclude-resource"),
            TryGetOptionValue(args, "--out") ?? TryGetOptionValue(args, "--output-directory") ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved),
            request.TargetContract);
    }

    private static PowerShellCompilationExplanation CreatePowerShellExplanation(string[] args, PowerShellAnalysisRequest request)
    {
        var targetFramework = request.TargetContract?.TargetFramework ?? TryGetOptionValue(args, "--framework") ?? "net8.0";
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            request.Path,
            request.Kind,
            request.Mode == PowerShellCompilationMode.Analyze ? null : request.Mode,
            allowDynamicModuleRuntimeSources: request.ResourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                              request.Mode != PowerShellCompilationMode.Strict);
        var plan = new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(), request.SemanticProfileId).Analyze(
            resolved,
            request.Mode,
            targetFramework,
            request.ResourceMode,
            GetOptionValues(args, "--include-resource"),
            GetOptionValues(args, "--exclude-resource"),
            TryGetOptionValue(args, "--out") ?? TryGetOptionValue(args, "--output-directory") ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved),
            request.TargetContract);
        return request.Mode == PowerShellCompilationMode.Analyze
            ? PowerShellCompilationExplanationService.Create(plan)
            : PowerShellCompilationExplainShaper.CreateFinalExplanation(resolved, plan, targetFramework);
    }

    private sealed record PowerShellAnalysisRequest(
        string Path,
        PowerShellCompilationArtifactKind? Kind,
        PowerShellCompilationMode Mode,
        PowerShellCompilationResourceMode ResourceMode,
        string SemanticProfileId,
        PowerShellCompilationTargetContract? TargetContract);
}
