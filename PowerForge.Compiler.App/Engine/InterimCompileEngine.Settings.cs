namespace PowerForge.Compiler.App.Engine;

/// <summary>
/// Settings files are the engine's own <c>powerforge.psproject.json</c>, read and written through
/// <see cref="PowerShellCompilationProjectManifestService"/>, so a file saved here builds the same way with
/// <c>powerforge powershell project build</c> in CI.
/// </summary>
internal sealed partial class InterimCompileEngine
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
            var kind = new PowerShellCompilationInputResolver().Resolve(inputPath).Kind;
            var deployment = request.Target.Deployment;
            var contract = PowerShellCompilationTargetContractService.Create(
                kind,
                request.Mode,
                request.Target.TargetFramework,
                request.Target.RuntimeIdentifier,
                selfContained: RequiresSelfContained(deployment),
                singleFile: true,
                optimization: deployment switch
                {
                    PowerShellCompilationDeploymentModel.Trimmed => PowerShellCompilationExecutableOptimization.Trimmed,
                    PowerShellCompilationDeploymentModel.NativeAot => PowerShellCompilationExecutableOptimization.NativeAot,
                    _ => PowerShellCompilationExecutableOptimization.None
                },
                explicitContract: true,
                semanticProfileId: GetSemanticProfileId(request.Target.TargetFramework));

            // A fresh manifest gives the engine's own name, lock path and output folder for this target.
            var fresh = service.Create(settingsPath, inputPath, Path.GetFileNameWithoutExtension(inputPath.TrimEnd('\\', '/')), contract);
            var artifact = fresh.Artifacts[0];
            var manifest = File.Exists(settingsPath) ? service.Load(settingsPath) : fresh;
            var existing = manifest.Artifacts.FirstOrDefault(saved => saved.Name.Equals(artifact.Name, StringComparison.Ordinal));
            if (existing is null)
            {
                manifest.Artifacts = manifest.Artifacts.Where(saved => !ReferenceEquals(saved, artifact)).Append(artifact).ToArray();
                existing = artifact;
            }

            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                var root = Path.GetDirectoryName(settingsPath) ?? Directory.GetCurrentDirectory();
                existing.OutputDirectory = Path.GetRelativePath(root, Path.GetFullPath(request.OutputDirectory)).Replace('\\', '/');
            }

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
        var input = !string.IsNullOrWhiteSpace(manifest.EntryPoint)
            ? manifest.EntryPoint!
            : manifest.Sources.Length == 1 ? manifest.Sources[0] : ".";
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
                    Path.GetFullPath(Path.Combine(root, artifact.OutputDirectory))))
                .ToArray());
    }

    private static CompileDiagnostic SettingsProblem(string path, string message) =>
        new(CompileSeverity.Error, "SettingsError", string.Empty, message, path, 0, 0);
}
