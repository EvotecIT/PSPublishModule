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
            var contract = CreateTarget(kind, request.Mode, request.Target);

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
                    Path.GetFullPath(Path.Combine(root, artifact.OutputDirectory)),
                    artifact.Target))
                .ToArray());
    }

    private static bool SameFullPath(string left, string right) =>
        Path.GetFullPath(left).TrimEnd('\\', '/').Equals(Path.GetFullPath(right).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static CompileDiagnostic SettingsProblem(string path, string message) =>
        new(CompileSeverity.Error, "SettingsError", string.Empty, message, path, 0, 0);
}
