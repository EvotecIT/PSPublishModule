using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private static async Task ValidateCliArtifactsAsync(CliArtifactValidation spec, Dictionary<string, string> variables,
        ReleaseValidationReport report, string[]? stagedAssets, DotNetPublishPlan? publishPlan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.Target))
            throw new InvalidOperationException("CLI validation requires a nonempty target identity.");
        if (spec.Runtimes is null || spec.Frameworks is null || spec.Styles is null ||
            spec.Runtimes.Concat(spec.Frameworks).Concat(spec.Styles).Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("CLI runtime, framework, and style identities must be nonempty strings.");
        var manifestPath = Resolve(spec.ManifestPath, variables);
        using var document = JsonDocument.Parse(await DotNetPublishReleaseArtifactVerifier.ReadBoundedTextAsync(
            manifestPath, "CLI artifact manifest", DotNetPublishReleaseArtifactVerifier.MaxManifestBytes,
            cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        var unified = GetProperty(document.RootElement, "assetEntries", out var entries);
        if (!unified) entries = document.RootElement;
        if (entries.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("CLI manifest must contain an artifact array.");
        var all = entries.EnumerateArray().ToArray();
        var artifacts = all.Where(entry => string.Equals(Text(entry, "category"), unified ? "Tool" : "Publish", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Text(entry, "target"), spec.Target, StringComparison.OrdinalIgnoreCase)).ToArray();
        var expected = new List<string>();
        var includeFramework = spec.PublishConfigPath is not null || spec.Frameworks.Length > 0;
        if (spec.PublishConfigPath is not null)
        {
            var path = Resolve(spec.PublishConfigPath, variables);
            var plan = publishPlan;
            if (plan is null || !plan.ConfigurationInputPaths.Contains(path,
                    FileSystemPathSafety.ExistingPathComparer))
            {
                var configured = await DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputsAsync(
                    path, cancellationToken).ConfigureAwait(false);
                plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(configured.Configuration, configured.InputPaths.Last(), enforceRequiredEnvironmentVariables: false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            var target = plan.Targets.SingleOrDefault(item => string.Equals(item.Name, spec.Target, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Publish configuration has no target '{spec.Target}'.");
            expected.AddRange(target.Combinations.Select(item => item.Runtime + "/" + item.Framework + "/" + item.Style));
        }
        else
        {
            foreach (var runtime in spec.Runtimes)
                foreach (var framework in includeFramework ? spec.Frameworks : new[] { string.Empty })
                    foreach (var style in spec.Styles) expected.Add(runtime + "/" + (includeFramework ? framework + "/" : string.Empty) + style);
        }
        if (expected.Count == 0 || expected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
            throw new InvalidOperationException("CLI validation requires an unambiguous runtime/style matrix.");
        var actual = artifacts.Select(item => Text(item, "runtime") + "/" +
            (includeFramework ? Text(item, "framework") + "/" : string.Empty) + Text(item, "style")).ToArray();
        if (actual.Length != expected.Count || expected.Any(pair => actual.Count(item => string.Equals(item, pair, StringComparison.OrdinalIgnoreCase)) != 1))
            throw new InvalidOperationException("CLI manifest does not match the configured runtime/style matrix.");
        var paths = new HashSet<string>(FileSystemPathSafety.ExistingPathComparer);
        var physicalArtifacts = new HashSet<string>(StringComparer.Ordinal);
        if (unified)
        {
            foreach (var artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var version = Text(artifact, "version");
                if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("CLI artifact has no version.");
                if (string.IsNullOrWhiteSpace(report.Version)) report.Version = version;
                if (NuGetVersion.Parse(version) != NuGetVersion.Parse(report.Version))
                    throw new InvalidOperationException("CLI artifact has an unexpected version.");
            }
            variables["Version"] = report.Version;
        }
        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaredPath = unified ? EffectiveReleaseAssetPath(artifact) : Text(artifact, "zipPath");
            var directoryArtifact = false;
            if (!unified && string.IsNullOrWhiteSpace(declaredPath))
            {
                declaredPath = Text(artifact, "exePath");
                if (string.IsNullOrWhiteSpace(declaredPath))
                {
                    declaredPath = Text(artifact, "outputDir");
                    directoryArtifact = true;
                }
            }
            if (string.IsNullOrWhiteSpace(declaredPath))
                throw new InvalidOperationException("CLI artifact does not declare an archive, executable, or output directory.");
            var path = Resolve(declaredPath, variables);
            if (variables.TryGetValue("StagingRoot", out var stagingRoot) && unified)
                ValidateStagedCliPath(Resolve(stagingRoot, variables), path);
            if (!directoryArtifact)
                FileSystemPathSafety.RejectReparsePoints(path, Path.GetDirectoryName(path), "CLI artifact");
            var exists = directoryArtifact
                ? Directory.Exists(path) && HasNonEmptyValidationFile(path, cancellationToken)
                : File.Exists(path) && new FileInfo(path).Length > 0;
            if (!paths.Add(path) || !exists)
                throw new InvalidOperationException($"CLI artifact is duplicated, missing, or empty: {path}");
            var identity = directoryArtifact ? ExistingFilePathIdentityResolver.ResolveDirectoryStatus(path).Identity
                : ExistingFilePathIdentityResolver.Resolve(path);
            if (!physicalArtifacts.Add(identity))
                throw new InvalidOperationException($"CLI artifact is duplicated: {path}");
        }
        if (spec.ToolsOnly)
        {
            if (!unified || all.Any(item => !new[] { "Tool", "Metadata" }.Contains(Text(item, "category"), StringComparer.OrdinalIgnoreCase)) ||
                all.Count(item => string.Equals(Text(item, "category"), "Tool", StringComparison.OrdinalIgnoreCase)) != artifacts.Length)
                throw new InvalidOperationException("Tools-only release contains an unexpected payload or target.");
            if (!all.Any(item => string.Equals(Text(item, "category"), "Metadata", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Tools-only release is missing build evidence.");
        }
        if (unified && (spec.ToolsOnly || stagedAssets is not null || variables.ContainsKey("StagingRoot")))
        {
            if (!variables.TryGetValue("StagingRoot", out var stagingRoot))
                throw new InvalidOperationException("Staged CLI validation requires a staging root.");
            var manifestPaths = new HashSet<string>(paths.Comparer);
            foreach (var entry in all)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var declaredPath = EffectiveReleaseAssetPath(entry);
                if (string.IsNullOrWhiteSpace(declaredPath))
                    throw new InvalidOperationException("Staged release evidence does not declare a path.");
                var path = Resolve(declaredPath, variables);
                ValidateStagedCliPath(Resolve(stagingRoot, variables), path);
                if (!manifestPaths.Add(path) || !File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidOperationException($"Staged release evidence is duplicated, missing, or empty: {path}");
            }
            if (stagedAssets is not null && (stagedAssets.Length != manifestPaths.Count ||
                !manifestPaths.SetEquals(stagedAssets.Select(path => Resolve(path, variables)))))
                throw new InvalidOperationException("The staged asset set does not match the release manifest.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        report.Checks.Add($"CLI {spec.Target}: {artifacts.Length} artifacts");
    }

    // Generated post-staging assets already live at Path; copied assets use StagedPath.
    private static string EffectiveReleaseAssetPath(JsonElement entry)
        => GetProperty(entry, "stagedPath", out var staged) && staged.ValueKind != JsonValueKind.Null
            ? Text(entry, "stagedPath") : Text(entry, "path");

    private static void ValidateStagedCliPath(string stagingRoot, string path)
    {
        var root = Path.GetFullPath(stagingRoot);
        Within(root, path);
        var volumeRoot = Path.GetPathRoot(root)!;
        if (root.Length > volumeRoot.Length) root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FileSystemPathSafety.RejectReparsePoints(path, root, "Staged CLI artifact");
    }

    private static bool GetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default;
        return false;
    }
    private static string Text(JsonElement element, string name)
        => GetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
