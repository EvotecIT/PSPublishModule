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
        _processRunner = processRunner ?? new ProcessRunner();
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
        var hasConfiguration = !string.IsNullOrWhiteSpace(action.ConfigPath);
        if (hasConfiguration && (action.Environment.Count > 0 || !string.IsNullOrWhiteSpace(action.WorkingDirectory) || action.PreferWindowsPowerShell))
            throw new InvalidOperationException("ConfigPath actions use the validation contract's command Environment, WorkingDirectory, and module Hosts; script process options cannot be applied to them.");
        var path = ResolvePath(configurationDirectory, hasConfiguration ? action.ConfigPath! : action.FilePath);
        if (!File.Exists(path)) throw new FileNotFoundException($"Staged-release validation input was not found: {path}", path);
        var workingDirectory = string.IsNullOrWhiteSpace(action.WorkingDirectory) ? configurationDirectory : ResolvePath(configurationDirectory, action.WorkingDirectory!);
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException(workingDirectory);
        var name = string.IsNullOrWhiteSpace(action.Name) ? Path.GetFileNameWithoutExtension(path) : action.Name!.Trim();
        _logger.Info($"Running staged-release validation '{name}'.");
        if (hasConfiguration) return RunConfiguration(action, context, path, name, cancellationToken);

        var contextDirectory = Path.Combine(Path.GetTempPath(), "PowerForge", "release-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contextDirectory);
        context.ActionName = name;
        context.ContextPath = Path.Combine(contextDirectory, "context.json");
        try
        {
            File.WriteAllText(context.ContextPath, JsonSerializer.Serialize(context), new UTF8Encoding(false));
            var environment = new Dictionary<string, string?>(action.Environment, StringComparer.OrdinalIgnoreCase)
            {
                ["POWERFORGE_CONTEXT"] = context.ContextPath,
                ["POWERFORGE_RELEASE_STAGE"] = context.Stage,
                ["POWERFORGE_RELEASE_VERSION"] = context.ResolvedVersion,
                ["POWERFORGE_RELEASE_MANIFEST"] = context.ReleaseManifestPath,
                ["POWERFORGE_RELEASE_CHECKSUMS"] = context.ReleaseChecksumsPath,
                ["POWERFORGE_RELEASE_STAGING_ROOT"] = context.StagingRoot,
                ["POWERFORGE_MODULE_STAGING_PATH"] = context.ModuleStagingPath
            };
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
        finally { Directory.Delete(contextDirectory, recursive: true); }
    }

    private PowerForgeReleaseValidationResult RunConfiguration(PowerForgeReleaseValidationAction action,
        PowerForgeReleaseValidationContext context, string path, string name, CancellationToken cancellationToken)
    {
        var spec = ReleaseValidationService.Load(path);
        if (!context.ModuleSelected) spec.Modules = Array.Empty<ModuleArtifactValidation>();
        if (!context.PackagesSelected)
        {
            spec.Packages = null;
            spec.Consumers = Array.Empty<PackageConsumerValidation>();
            spec.Tools = Array.Empty<DotNetToolValidation>();
        }
        if (!context.ToolsSelected) spec.CliArtifacts = null;
        var request = new ReleaseValidationRequest { ProjectRoot = context.ProjectRoot, Version = context.ResolvedVersion,
            StagedAssets = context.StagedAssets.Length > 0 ? context.StagedAssets : null };
        if (spec.CliArtifacts is not null && !context.ModuleSelected && !context.PackagesSelected)
        {
            spec.CliArtifacts.ToolsOnly = true;
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(action.TimeoutSeconds));
        try
        {
            var report = new ReleaseValidationService(_processRunner).RunAsync(spec, path, request, timeout.Token).GetAwaiter().GetResult();
            return new PowerForgeReleaseValidationResult
            {
                Name = name, Succeeded = report.Success, ExitCode = report.Success ? 0 : 1,
                Executable = "PowerForge", FilePath = path, WorkingDirectory = context.ProjectRoot,
                StdOut = string.Join(Environment.NewLine, report.Checks), StdErr = string.Join(Environment.NewLine, report.Errors)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PowerForgeReleaseValidationResult { Name = name, FilePath = path, ExitCode = -1, TimedOut = true };
        }
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
