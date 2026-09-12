namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void ValidateReleaseValidationConfiguration(
        PowerForgeReleaseValidationOptions? validation,
        PowerForgeReleaseOutputsOptions outputs,
        PowerForgeReleaseRequest request,
        string configurationDirectory)
    {
        var actions = GetAfterStagingValidationActions(validation);
        if (actions.Length == 0)
            return;
        if (string.IsNullOrWhiteSpace(request.StageRoot) &&
            (outputs?.Staging is null || string.IsNullOrWhiteSpace(outputs.Staging.RootPath)))
        {
            throw new InvalidOperationException(
                "Validation.AfterStaging requires Outputs.Staging.RootPath so the complete release can be inspected before publication.");
        }

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.FilePath) == string.IsNullOrWhiteSpace(action.ConfigPath))
                throw new InvalidOperationException("Validation.AfterStaging actions require exactly one of FilePath or ConfigPath.");
            if (action.TimeoutSeconds <= 0)
                throw new InvalidOperationException("Validation.AfterStaging action TimeoutSeconds must be greater than zero.");
            PowerForgeReleaseValidationService.ValidateActionEnvironment(action.Environment);
            if (!string.IsNullOrWhiteSpace(action.ConfigPath) &&
                (action.Environment.Count > 0 || !string.IsNullOrWhiteSpace(action.WorkingDirectory) || action.PreferWindowsPowerShell))
                throw new InvalidOperationException("ConfigPath actions use the validation contract's command Environment, WorkingDirectory, and module Hosts; script process options cannot be applied to them.");
            var scriptPath = ResolveValidationPath(configurationDirectory, string.IsNullOrWhiteSpace(action.ConfigPath) ? action.FilePath : action.ConfigPath!);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Staged-release validation script was not found: {scriptPath}", scriptPath);
            if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
            {
                var workingDirectory = ResolveValidationPath(configurationDirectory, action.WorkingDirectory!);
                if (!Directory.Exists(workingDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"Staged-release validation working directory was not found: {workingDirectory}");
                }
            }
        }
    }

    private bool ExecuteAfterStagingValidations(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configurationDirectory,
        PowerForgeReleaseResult result,
        string? resolvedVersion,
        bool moduleSelected,
        bool packagesSelected,
        bool toolsSelected)
    {
        var actions = GetAfterStagingValidationActions(spec.Validation);
        if (actions.Length == 0)
            return true;

        request.Progress?.PhaseStarted(
            PowerForgeReleaseProgressPhase.Validation,
            actions.Length,
            "Validating the complete staged release");
        var validationResults = new List<PowerForgeReleaseValidationResult>(actions.Length);
        ReleaseValidationIntegrityCheckpoint? integrity = null;
        foreach (var action in actions)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var context = new PowerForgeReleaseValidationContext
            {
                ConfigPath = result.ConfigPath,
                ProjectRoot = ResolveValidationProjectRoot(result, configurationDirectory),
                ResolvedVersion = resolvedVersion ?? ResolveModuleReleaseVersion(result.ModulePlan) ??
                    ResolveUniqueAssetVersion(result.ReleaseAssetEntries.Where(asset => asset.Category is
                        PowerForgeReleaseAssetCategory.Tool or PowerForgeReleaseAssetCategory.Portable or
                        PowerForgeReleaseAssetCategory.Installer or PowerForgeReleaseAssetCategory.Store)) ?? string.Empty,
                ReleaseManifestPath = result.ReleaseManifestPath,
                ReleaseChecksumsPath = result.ReleaseChecksumsPath,
                StagingRoot = ResolveConfiguredStageRoot(spec, request, configurationDirectory),
                ModuleStagingPath = result.ModulePlan?.StagingPath,
                ReleaseAssets = result.ReleaseAssets.ToArray(),
                AssetEntries = result.ReleaseAssetEntries.ToArray(),
                ModuleSelected = moduleSelected,
                PackagesSelected = packagesSelected,
                ToolsSelected = toolsSelected,
                ToolArtifactsSelected = toolsSelected && ResolveSelectedToolOutputs(request).Contains(PowerForgeReleaseToolOutputKind.Tool),
                PublishPlan = toolsSelected ? result.DotNetToolPlan : null,
                SelectedToolTargets = NormalizeStrings(request.Targets).Length == 0 ? null :
                    result.DotNetToolPlan?.Targets.Select(target => target.Name).ToArray() ??
                    result.ToolPlan?.Targets.Select(target => target.Name).ToArray(),
                StagedAssets = result.ReleaseAssetEntries
                    .Select(static asset => asset.StagedPath ?? asset.Path)
                    .Distinct(PathComparer)
                    .ToArray()
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(action.TimeoutSeconds));
            PowerForgeReleaseValidationResult validation;
            try
            {
                integrity ??= CaptureValidationIntegrityCheckpoint(result, timeout.Token);
                validation = _runReleaseValidation(action, context, configurationDirectory, timeout.Token);
                if (validation.Succeeded)
                {
                    ValidateIntegrityUnchanged(integrity.Paths, integrity.Hashes, timeout.Token);
                }
            }
            catch (Exception exception) when (!request.CancellationToken.IsCancellationRequested)
            {
                validation = new PowerForgeReleaseValidationResult
                {
                    Name = action.Name ?? "Release validation", ExitCode = 1,
                    StdErr = exception.Message, TimedOut = timeout.IsCancellationRequested
                };
            }
            request.CancellationToken.ThrowIfCancellationRequested();
            validationResults.Add(validation);
            result.ReleaseValidations = validationResults.ToArray();
            if (validation.Succeeded)
                continue;

            var detail = BuildReleaseValidationFailure(validation);
            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Validation, detail);
            result.Success = false;
            result.ErrorMessage = detail;
            return false;
        }

        result.ReleaseValidationIntegrity = integrity;

        request.Progress?.PhaseCompleted(
            PowerForgeReleaseProgressPhase.Validation,
            $"{validationResults.Count} staged-release validation action(s) passed");
        return true;
    }

    private static string BuildReleaseValidationFailure(PowerForgeReleaseValidationResult result)
    {
        var detail = string.Join(
            Environment.NewLine,
            new[] { result.StdErr, result.StdOut }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim()));
        var summary = result.TimedOut
            ? $"Staged-release validation '{result.Name}' timed out."
            : $"Staged-release validation '{result.Name}' failed with exit code {result.ExitCode}.";
        return string.IsNullOrWhiteSpace(detail)
            ? summary
            : summary + Environment.NewLine + detail;
    }

    private bool PublishToolGitHubAfterStaging(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configurationDirectory,
        PowerForgeReleaseResult result,
        string? resolvedVersion)
    {
        if (spec.Tools is null || !(request.PublishToolGitHub ?? spec.Tools.GitHub.Publish))
            return true;

        if (!ValidateReleaseValidationIntegrityBeforePublication(
                request,
                result,
                PowerForgeReleaseProgressPhase.Tools))
        {
            return false;
        }

        ValidatePostBuildSourceState(request);
        request.CancellationToken.ThrowIfCancellationRequested();
        if (result.DotNetToolPlan is not null && result.DotNetTools is not null)
        {
            result.ToolGitHubReleases = PublishDotNetToolGitHubReleases(
                spec,
                configurationDirectory,
                result.DotNetToolPlan,
                result.DotNetTools,
                resolvedVersion,
                request.CancellationToken);
        }
        else if (result.Tools is not null)
        {
            result.ToolGitHubReleases = PublishLegacyToolGitHubReleases(
                spec,
                configurationDirectory,
                result.Tools,
                request.CancellationToken);
        }

        var failure = result.ToolGitHubReleases.FirstOrDefault(static release => !release.Success);
        if (failure is null)
            return true;

        request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Tools, failure.ErrorMessage);
        result.Success = false;
        result.ErrorMessage = failure.ErrorMessage ?? "Tool GitHub release publishing failed.";
        return false;
    }

    private static PowerForgeReleaseValidationAction[] GetAfterStagingValidationActions(
        PowerForgeReleaseValidationOptions? validation)
        => (validation?.AfterStaging ?? Array.Empty<PowerForgeReleaseValidationAction>())
            .Where(static action => action is not null && action.Enabled)
            .ToArray();

    private static bool HasAfterStagingValidation(PowerForgeReleaseSpec spec)
        => GetAfterStagingValidationActions(spec.Validation).Length > 0;

    private static string ResolveValidationProjectRoot(
        PowerForgeReleaseResult result,
        string configurationDirectory)
        => new[] { result.ModulePlan?.RepositoryRoot, result.Packages?.RootPath, result.DotNetToolPlan?.ProjectRoot,
                result.ToolPlan?.ProjectRoot, result.AppleAppPlan?.ProjectRoot }
            .FirstOrDefault(root => !string.IsNullOrWhiteSpace(root)) ?? configurationDirectory;

    private static string ResolveValidationPath(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));

    private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\'
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
