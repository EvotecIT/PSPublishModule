using System.Text.Json;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private static void ValidateCliArtifacts(CliArtifactValidation spec, Dictionary<string, string> variables, ReleaseValidationReport report, string[]? stagedAssets)
    {
        var manifestPath = Resolve(spec.ManifestPath, variables);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
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
            var configured = DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputs(path);
            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(configured.Configuration, configured.InputPaths.Last(), enforceRequiredEnvironmentVariables: false);
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
        var paths = new HashSet<string>(FrameworkCompatibility.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (unified)
        {
            foreach (var artifact in artifacts)
            {
                var version = Text(artifact, "version");
                if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("CLI artifact has no version.");
                if (string.IsNullOrWhiteSpace(report.Version)) report.Version = version;
                if (!string.Equals(version, report.Version, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("CLI artifact has an unexpected version.");
            }
            variables["Version"] = report.Version;
        }
        foreach (var artifact in artifacts)
        {
            var declaredPath = Text(artifact, unified ? "stagedPath" : "zipPath");
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
            var exists = directoryArtifact
                ? Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(file => new FileInfo(file).Length > 0)
                : File.Exists(path) && new FileInfo(path).Length > 0;
            if (!paths.Add(path) || !exists)
                throw new InvalidOperationException($"CLI artifact is duplicated, missing, or empty: {path}");
            if (variables.TryGetValue("StagingRoot", out var stagingRoot) && unified)
                Within(stagingRoot, DotNetPublishReleaseArtifactVerifier.GetRelativePath(stagingRoot, path));
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
                var path = Resolve(Text(entry, "stagedPath"), variables);
                Within(stagingRoot, DotNetPublishReleaseArtifactVerifier.GetRelativePath(stagingRoot, path));
                if (!manifestPaths.Add(path) || !File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidOperationException($"Staged release evidence is duplicated, missing, or empty: {path}");
            }
            if (stagedAssets is not null && (stagedAssets.Length != manifestPaths.Count ||
                !manifestPaths.SetEquals(stagedAssets.Select(path => Resolve(path, variables)))))
                throw new InvalidOperationException("The staged asset set does not match the release manifest.");
        }
        report.Checks.Add($"CLI {spec.Target}: {artifacts.Length} artifacts");
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
