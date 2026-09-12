using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Adapts staged release context to shared validation or a product-owned PowerShell probe.</summary>
internal sealed class PowerForgeReleaseValidationService
{
    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;

    internal PowerForgeReleaseValidationService(ILogger logger, IProcessRunner? processRunner = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = new ReleaseValidationProcessRunner(processRunner);
    }

    internal PowerForgeReleaseValidationResult Run(PowerForgeReleaseValidationAction action,
        PowerForgeReleaseValidationContext context, string configurationDirectory, CancellationToken cancellationToken)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(configurationDirectory)) throw new ArgumentException("Configuration directory is required.", nameof(configurationDirectory));
        if (string.IsNullOrWhiteSpace(action.FilePath) == string.IsNullOrWhiteSpace(action.ConfigPath))
            throw new InvalidOperationException("A staged-release validation action requires exactly one of FilePath or ConfigPath.");
        if (action.TimeoutSeconds <= 0) throw new InvalidOperationException("A staged-release validation action TimeoutSeconds must be greater than zero.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(action.TimeoutSeconds));
        try
        {
            return RunAction(action, context, configurationDirectory, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            var path = ResolvePath(configurationDirectory, string.IsNullOrWhiteSpace(action.ConfigPath) ? action.FilePath : action.ConfigPath!);
            return new PowerForgeReleaseValidationResult {
                Name = string.IsNullOrWhiteSpace(action.Name) ? Path.GetFileNameWithoutExtension(path) : action.Name!.Trim(),
                FilePath = path, ExitCode = -1, TimedOut = true
            };
        }
    }

    private PowerForgeReleaseValidationResult RunAction(PowerForgeReleaseValidationAction action,
        PowerForgeReleaseValidationContext context, string configurationDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasConfiguration = !string.IsNullOrWhiteSpace(action.ConfigPath);
        if (hasConfiguration && (action.Environment.Count > 0 || !string.IsNullOrWhiteSpace(action.WorkingDirectory) || action.PreferWindowsPowerShell))
            throw new InvalidOperationException("ConfigPath actions use the validation contract's command Environment, WorkingDirectory, and module Hosts; script process options cannot be applied to them.");
        var path = ResolvePath(configurationDirectory, hasConfiguration ? action.ConfigPath! : action.FilePath);
        if (!File.Exists(path)) throw new FileNotFoundException($"Staged-release validation input was not found: {path}", path);
        var workingDirectory = string.IsNullOrWhiteSpace(action.WorkingDirectory) ? configurationDirectory : ResolvePath(configurationDirectory, action.WorkingDirectory!);
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException(workingDirectory);
        var name = string.IsNullOrWhiteSpace(action.Name) ? Path.GetFileNameWithoutExtension(path) : action.Name!.Trim();
        _logger.Info($"Running staged-release validation '{name}'.");
        cancellationToken.ThrowIfCancellationRequested();
        if (hasConfiguration) return RunConfiguration(action, context, path, name, cancellationToken);

        var contextDirectory = Path.Combine(Path.GetTempPath(), "PowerForge", "release-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contextDirectory);
        context.ActionName = name;
        context.ContextPath = Path.Combine(contextDirectory, "context.json");
        try
        {
            File.WriteAllText(context.ContextPath, JsonSerializer.Serialize(context), new UTF8Encoding(false));
            var environment = new Dictionary<string, string?>(
                FrameworkCompatibility.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var pair in action.Environment) environment[pair.Key] = pair.Value;
            environment["POWERFORGE_CONTEXT"] = context.ContextPath;
            environment["POWERFORGE_RELEASE_STAGE"] = context.Stage;
            environment["POWERFORGE_RELEASE_VERSION"] = context.ResolvedVersion;
            environment["POWERFORGE_RELEASE_MANIFEST"] = context.ReleaseManifestPath;
            environment["POWERFORGE_RELEASE_CHECKSUMS"] = context.ReleaseChecksumsPath;
            environment["POWERFORGE_RELEASE_STAGING_ROOT"] = context.StagingRoot;
            environment["POWERFORGE_MODULE_STAGING_PATH"] = context.ModuleStagingPath;
            var executable = ResolvePowerShellExecutable(action.PreferWindowsPowerShell);
            var process = _processRunner.RunAsync(new ProcessRunRequest(executable, workingDirectory,
                new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", path },
                TimeSpan.FromSeconds(action.TimeoutSeconds), environment), cancellationToken).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return new PowerForgeReleaseValidationResult
            {
                Name = name, Succeeded = process.Succeeded, ExitCode = process.ExitCode, Executable = executable,
                FilePath = path, WorkingDirectory = workingDirectory, StdOut = process.StdOut, StdErr = process.StdErr,
                TimedOut = process.TimedOut
            };
        }
        finally { ValidationDirectoryCleanup.TryDelete(contextDirectory, _logger.Warn); }
    }

    private PowerForgeReleaseValidationResult RunConfiguration(PowerForgeReleaseValidationAction action,
        PowerForgeReleaseValidationContext context, string path, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var spec = ReleaseValidationService.LoadAsync(path, cancellationToken).GetAwaiter().GetResult();
        var hadContracts = ReleaseValidationService.HasContracts(spec);
        if (!context.ModuleSelected) spec.Modules = Array.Empty<ModuleArtifactValidation>();
        if (!context.PackagesSelected)
        {
            spec.Packages = null;
            spec.Consumers = Array.Empty<PackageConsumerValidation>();
            spec.Tools = Array.Empty<DotNetToolValidation>();
        }
        if (!context.ToolsSelected || !context.ToolArtifactsSelected) spec.CliArtifacts = null;
        string? skippedTarget = null;
        if (spec.CliArtifacts is not null && context.SelectedToolTargets is not null &&
            !context.SelectedToolTargets.Contains(spec.CliArtifacts.Target, StringComparer.OrdinalIgnoreCase))
        {
            skippedTarget = $"Skipped: CLI target '{spec.CliArtifacts.Target}' was not selected for this release.";
            spec.CliArtifacts = null;
        }
        // Keep JSON path semantics identical to standalone validation, including explicitly separate lane roots.
        var projectRoot = ResolvePath(Path.GetDirectoryName(path)!, spec.ProjectRoot);
        if (hadContracts && spec.SchemaVersion == 1 && !ReleaseValidationService.HasContracts(spec))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PowerForgeReleaseValidationResult
            {
                Name = name, Succeeded = true, ExitCode = 0, Executable = "PowerForge", FilePath = path,
                WorkingDirectory = projectRoot, StdOut = skippedTarget ?? "Skipped: validation contracts belong to unselected release lanes."
            };
        }
        var request = new ReleaseValidationRequest { Version = context.ResolvedVersion, PublishPlan = context.PublishPlan,
            StagedAssets = context.StagedAssets.Length > 0 ? context.StagedAssets : null };
        if (spec.CliArtifacts is not null && !context.ModuleSelected && !context.PackagesSelected)
        {
            var versions = context.AssetEntries.Where(entry => entry.Category == PowerForgeReleaseAssetCategory.Tool &&
                    string.Equals(entry.Target, spec.CliArtifacts.Target, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Version).Where(version => !string.IsNullOrWhiteSpace(version)).Distinct().ToArray();
            if (versions.Length == 1) request.Version = versions[0];
            else if (versions.Length > 1) throw new InvalidOperationException("The staged CLI target contains conflicting release versions.");
        }
        request.Variables["ReleaseConfigPath"] = context.ConfigPath;
        SetVariable(request, "ReleaseManifestPath", context.ReleaseManifestPath);
        SetVariable(request, "ReleaseChecksumsPath", context.ReleaseChecksumsPath);
        SetVariable(request, "StagingRoot", context.StagingRoot);
        SetVariable(request, "ModuleStagingPath", context.ModuleStagingPath);
        var modules = context.AssetEntries.Where(entry => entry.Category == PowerForgeReleaseAssetCategory.Module &&
            !string.IsNullOrWhiteSpace(entry.StagedPath) && entry.StagedPath!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (modules.Length == 1) request.Variables["ModuleArchive"] = modules[0].StagedPath!;
        var packageRoots = context.AssetEntries.Where(entry => entry.Category == PowerForgeReleaseAssetCategory.Package &&
                !string.IsNullOrWhiteSpace(entry.StagedPath) && entry.StagedPath!.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(entry => Path.GetDirectoryName(entry.StagedPath!)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (packageRoots.Length == 1) request.Variables["PackageRoot"] = packageRoots[0];
        var report = new ReleaseValidationService(_processRunner).RunAsync(spec, path, request, cancellationToken).GetAwaiter().GetResult();
        if (skippedTarget is not null) report.Checks.Insert(0, skippedTarget);
        return new PowerForgeReleaseValidationResult
        {
            Name = name, Succeeded = report.Success, ExitCode = report.Success ? 0 : 1,
            Executable = "PowerForge", FilePath = path, WorkingDirectory = projectRoot,
            StdOut = string.Join(Environment.NewLine, report.Checks), StdErr = string.Join(Environment.NewLine, report.Errors)
        };
    }

    private static void SetVariable(ReleaseValidationRequest request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) request.Variables[name] = value!;
    }
    private static string ResolvePowerShellExecutable(bool preferWindowsPowerShell)
    {
        if (FrameworkCompatibility.IsWindows() && preferWindowsPowerShell)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(path)) return path;
        }
        return "pwsh";
    }
    private static string ResolvePath(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}
