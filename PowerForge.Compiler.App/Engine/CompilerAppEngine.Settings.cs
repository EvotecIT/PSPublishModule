namespace PowerForge.Compiler.App.Engine;

/// <summary>Reads and writes single-input settings through the compiler's portable project manifest.</summary>
internal sealed partial class CompilerAppEngine
{
    /// <summary>The file name the engine's project commands look for.</summary>
    internal const string DefaultSettingsFileName = "powerforge.psproject.json";

    public SettingsResult OpenSettings(string settingsPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(settingsPath.Trim().Trim('"'));
            var manifest = new PowerShellCompilationProjectManifestService().Load(fullPath);
            return new SettingsResult(ToSettings(fullPath, manifest), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SettingsResult(null, SettingsProblem(settingsPath, $"These settings can't be opened: {ex.Message}"));
        }
    }

    public SettingsResult SaveSettings(SaveSettingsRequest request)
    {
        try
        {
            var settingsPath = Path.GetFullPath(request.SettingsPath.Trim().Trim('"'));
            var inputPath = Path.GetFullPath(request.InputPath);
            var service = new PowerShellCompilationProjectManifestService();
            var spec = Compiler.Prepare(CreateRequest(inputPath, request.Mode, request.Files, request.Target));
            var contract = spec.TargetContract ?? CreateTarget(spec.Kind, spec.Mode, request.Target);

            // A fresh manifest gives the engine's own name, lock path and output folder for this target.
            var fresh = service.Create(settingsPath, inputPath, Path.GetFileNameWithoutExtension(inputPath.TrimEnd('\\', '/')), contract);
            var artifact = fresh.Artifacts[0];
            var manifest = File.Exists(settingsPath) ? service.Load(settingsPath) : fresh;
            if (!ReferenceEquals(manifest, fresh))
            {
                // A settings file describes one script; never write another script's targets into it.
                var owner = ToSettings(settingsPath, manifest).InputPath;
                if (!SameFullPath(owner, inputPath))
                    return new SettingsResult(null, SettingsProblem(request.SettingsPath,
                        $"{Path.GetFileName(settingsPath)} already holds settings for {Path.GetFileName(owner)}. Use Save as to keep settings for {Path.GetFileName(inputPath)} in another file."));
            }

            var existing = manifest.Artifacts.FirstOrDefault(saved => saved.Name.Equals(artifact.Name, StringComparison.Ordinal)) ?? artifact;

            existing.Target = artifact.Target;

            // A blank output folder means the engine default, which also clears an earlier override.
            var root = Path.GetDirectoryName(settingsPath) ?? Directory.GetCurrentDirectory();
            existing.OutputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? artifact.OutputDirectory
                : Path.GetRelativePath(root, Path.GetFullPath(request.OutputDirectory)).Replace('\\', '/');

            // The most recently saved target goes first, so opening the file restores it; the others stay.
            manifest.Artifacts = new[] { existing }
                .Concat(manifest.Artifacts.Where(saved => !ReferenceEquals(saved, existing) && !saved.Name.Equals(existing.Name, StringComparison.Ordinal)))
                .ToArray();

            manifest.Resources = new PowerShellCompilationProjectResourcePolicy
            {
                Mode = request.Files.Mode,
                Include = request.Files.Include.ToArray(),
                Exclude = request.Files.Exclude.ToArray()
            };
            service.Save(settingsPath, manifest);
            return OpenSettings(settingsPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SettingsResult(null, SettingsProblem(request.SettingsPath, $"The settings weren't saved: {ex.Message}"));
        }
    }

    internal static BuildSettings ToSettings(string settingsPath, PowerShellCompilationProjectManifest manifest)
    {
        var root = Path.GetDirectoryName(settingsPath) ?? Directory.GetCurrentDirectory();
        if (manifest.Sources.Length != 1)
            throw new InvalidOperationException("The app opens settings for one script or module. Use powerforge powershell project for a loose source set.");
        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
            throw new InvalidOperationException("Use powerforge powershell project to build a project with an explicit entrypoint.");
        if (manifest.Artifacts.Any(static artifact => artifact.ModuleTest is not null))
            throw new InvalidOperationException("Use powerforge powershell project for a project with a required module acceptance script.");
        if (HasCustomDiagnostics(manifest.Diagnostics))
            throw new InvalidOperationException("Use powerforge powershell project to build with a project-specific diagnostics policy.");
        if (manifest.ProviderPackages.Length != 0 || manifest.Artifacts.Any(static artifact =>
                artifact.ProviderLock is not null || artifact.ExpectedAbiSha256 is not null ||
                artifact.ArtifactName is not null || artifact.EmitSource || artifact.EmitIr))
            throw new InvalidOperationException("Use powerforge powershell project to build with project-specific artifact settings, providers, or ABI locks.");
        var input = manifest.Sources[0];
        var resources = manifest.Resources ?? new PowerShellCompilationProjectResourcePolicy();
        return new BuildSettings(
            settingsPath,
            Path.GetFullPath(Path.Combine(root, input)),
            new FileRules(resources.Mode, resources.Include ?? Array.Empty<string>(), resources.Exclude ?? Array.Empty<string>()),
            manifest.Artifacts
                .Select(artifact => new SavedTarget(
                    artifact.Name,
                    artifact.Target.Mode,
                    artifact.Target.TargetFramework,
                    artifact.Target.RuntimeIdentifier,
                    artifact.Target.Deployment,
                    Path.GetFullPath(Path.Combine(root, artifact.OutputDirectory)),
                    artifact.Target))
                .ToArray());
    }

    private static bool HasCustomDiagnostics(PowerShellCompilationDiagnosticsPolicy policy)
    {
        var defaults = new PowerShellCompilationDiagnosticsPolicy();
        return policy.SchemaVersion != defaults.SchemaVersion ||
            policy.LocalOnly != defaults.LocalOnly ||
            policy.AutomaticUpload != defaults.AutomaticUpload ||
            policy.ArtifactEvidenceRetention != defaults.ArtifactEvidenceRetention ||
            policy.RecommendedFailureBundleRetentionDays != defaults.RecommendedFailureBundleRetentionDays ||
            policy.RedactedData is { Length: > 0 };
    }

    private static bool SameFullPath(string left, string right) =>
        Path.GetFullPath(left).TrimEnd('\\', '/').Equals(Path.GetFullPath(right).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static CompileDiagnostic SettingsProblem(string path, string message) =>
        new(CompileSeverity.Error, "SettingsError", string.Empty, message, path, 0, 0);
}
