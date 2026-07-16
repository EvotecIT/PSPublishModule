using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed class HomeAssistantRepositoryService {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IProcessRunner _processRunner;

    internal HomeAssistantRepositoryService(IProcessRunner? processRunner = null) {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    internal HomeAssistantRepositorySnapshot Inspect(string repositoryRoot) {
        var root = ResolveRoot(repositoryRoot);
        var hacsPath = Path.Combine(root, "hacs.json");
        if (!File.Exists(hacsPath))
            throw new InvalidOperationException($"HACS metadata was not found at '{hacsPath}'.");

        var hacs = ReadJsonObject(hacsPath);
        var hacsFileName = ReadOptionalString(hacs, "filename");
        var zipRelease = ReadOptionalBoolean(hacs, "zip_release");
        var packageJsonPath = Path.Combine(root, "package.json");

        if (!string.IsNullOrWhiteSpace(hacsFileName) && File.Exists(packageJsonPath)) {
            var package = ReadJsonObject(packageJsonPath);
            var version = ReadRequiredString(package, "version", packageJsonPath);
            return new HomeAssistantRepositorySnapshot {
                Kind = HomeAssistantRepositoryKind.LovelacePlugin,
                Version = version,
                PackageJsonPath = packageJsonPath,
                PackageLockPath = File.Exists(Path.Combine(root, "package-lock.json")) ? Path.Combine(root, "package-lock.json") : null,
                HacsPath = hacsPath,
                HacsFileName = hacsFileName,
                ZipRelease = false
            };
        }

        var customComponents = Path.Combine(root, "custom_components");
        if (!Directory.Exists(customComponents))
            throw new InvalidOperationException("The repository is neither a HACS Lovelace plugin nor a Home Assistant custom integration.");

        var manifests = Directory.GetFiles(customComponents, "manifest.json", SearchOption.AllDirectories);
        if (manifests.Length != 1)
            throw new InvalidOperationException($"Expected exactly one custom integration manifest under '{customComponents}', but found {manifests.Length}.");

        var manifestPath = manifests[0];
        var manifest = ReadJsonObject(manifestPath);
        var manifestVersion = ReadRequiredString(manifest, "version", manifestPath);
        var pyProjectPath = Path.Combine(root, "pyproject.toml");
        if (File.Exists(pyProjectPath)) {
            var projectVersion = ReadPyProjectVersion(pyProjectPath);
            if (!string.Equals(projectVersion, manifestVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Version drift detected: manifest.json has {manifestVersion}, while pyproject.toml has {projectVersion}.");
        }

        if (zipRelease && string.IsNullOrWhiteSpace(hacsFileName))
            throw new InvalidOperationException("hacs.json must define filename when zip_release is true.");

        return new HomeAssistantRepositorySnapshot {
            Kind = HomeAssistantRepositoryKind.Integration,
            Version = manifestVersion,
            IntegrationDirectory = Path.GetDirectoryName(manifestPath),
            ManifestPath = manifestPath,
            PyProjectPath = File.Exists(pyProjectPath) ? pyProjectPath : null,
            HacsPath = hacsPath,
            HacsFileName = hacsFileName,
            ZipRelease = zipRelease
        };
    }

    internal IReadOnlyList<string> UpdateVersion(HomeAssistantRepositorySnapshot snapshot, string repositoryRoot, string version) {
        _ = HomeAssistantSemanticVersion.Parse(version);
        var changed = new List<string>();

        if (snapshot.Kind == HomeAssistantRepositoryKind.Integration) {
            UpdateJsonVersion(snapshot.ManifestPath!, version);
            changed.Add(ToRelativePath(repositoryRoot, snapshot.ManifestPath!));
            if (!string.IsNullOrWhiteSpace(snapshot.PyProjectPath)) {
                UpdatePyProjectVersion(snapshot.PyProjectPath!, version);
                changed.Add(ToRelativePath(repositoryRoot, snapshot.PyProjectPath!));
            }
        } else if (snapshot.Kind == HomeAssistantRepositoryKind.LovelacePlugin) {
            UpdateJsonVersion(snapshot.PackageJsonPath!, version);
            changed.Add(ToRelativePath(repositoryRoot, snapshot.PackageJsonPath!));
            if (!string.IsNullOrWhiteSpace(snapshot.PackageLockPath)) {
                UpdatePackageLockVersion(snapshot.PackageLockPath!, version);
                changed.Add(ToRelativePath(repositoryRoot, snapshot.PackageLockPath!));
            }
        } else {
            throw new InvalidOperationException("The repository layout must be resolved before updating its version.");
        }

        return changed;
    }

    internal IReadOnlyList<string> BuildAssets(HomeAssistantRepositorySnapshot snapshot, string repositoryRoot, string version) {
        _ = HomeAssistantSemanticVersion.Parse(version);
        var root = ResolveRoot(repositoryRoot);
        if (snapshot.Kind == HomeAssistantRepositoryKind.LovelacePlugin) {
            Run(root, "npm", "ci");
            Run(root, "npm", "test");
            Run(root, "npm", "run", "check");
            Run(root, "npm", "run", "pack");

            var primaryAsset = Path.Combine(root, "release", snapshot.HacsFileName!);
            if (!File.Exists(primaryAsset))
                throw new InvalidOperationException($"The HACS plugin build did not produce '{primaryAsset}'.");

            return new[] { primaryAsset };
        }

        if (snapshot.Kind == HomeAssistantRepositoryKind.Integration && snapshot.ZipRelease) {
            var outputDirectory = Path.Combine(root, ".powerforge", "release");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, snapshot.HacsFileName!);
            if (File.Exists(outputPath)) File.Delete(outputPath);
            ZipFile.CreateFromDirectory(snapshot.IntegrationDirectory!, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return new[] { outputPath };
        }

        return Array.Empty<string>();
    }

    private void Run(string workingDirectory, string executable, params string[] arguments) {
        var result = _processRunner.RunAsync(
                new ProcessRunRequest(executable, workingDirectory, arguments, TimeSpan.FromMinutes(15)))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded) {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"{executable} {string.Join(" ", arguments)} failed with exit code {result.ExitCode}. {detail.Trim()}");
        }
    }

    private static string ResolveRoot(string repositoryRoot) {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("RepositoryRoot is required.", nameof(repositoryRoot));
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository root not found: {root}");
        return root;
    }

    private static JsonObject ReadJsonObject(string path) {
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        return node ?? throw new InvalidOperationException($"Expected a JSON object in '{path}'.");
    }

    private static string ReadRequiredString(JsonObject value, string propertyName, string path)
        => ReadOptionalString(value, propertyName)
           ?? throw new InvalidOperationException($"'{path}' must define a non-empty '{propertyName}' string.");

    private static string? ReadOptionalString(JsonObject value, string propertyName) {
        var result = value[propertyName]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(result) ? null : result!.Trim();
    }

    private static bool ReadOptionalBoolean(JsonObject value, string propertyName)
        => value[propertyName]?.GetValue<bool>() == true;

    private static void UpdateJsonVersion(string path, string version) {
        var value = ReadJsonObject(path);
        value["version"] = version;
        WriteJson(path, value);
    }

    private static void UpdatePackageLockVersion(string path, string version) {
        var value = ReadJsonObject(path);
        value["version"] = version;
        if (value["packages"] is JsonObject packages && packages[""] is JsonObject rootPackage)
            rootPackage["version"] = version;
        else
            throw new InvalidOperationException($"'{path}' does not contain packages[''] metadata.");
        WriteJson(path, value);
    }

    private static void WriteJson(string path, JsonObject value)
        => File.WriteAllText(path, value.ToJsonString(JsonOptions) + Environment.NewLine, Utf8NoBom);

    private static string ReadPyProjectVersion(string path) {
        var match = Regex.Match(
            File.ReadAllText(path),
            @"(?ms)^\[project\]\s*$.*?^version\s*=\s*""(?<version>[^""]+)""");
        if (!match.Success)
            throw new InvalidOperationException($"'{path}' does not define [project] version.");
        return match.Groups["version"].Value.Trim();
    }

    private static void UpdatePyProjectVersion(string path, string version) {
        var text = File.ReadAllText(path);
        var pattern = @"(?ms)(?<prefix>^\[project\]\s*$.*?^version\s*=\s*"")(?<version>[^""]+)(?<suffix>"")";
        var updated = new Regex(pattern).Replace(
            text,
            match => match.Groups["prefix"].Value + version + match.Groups["suffix"].Value,
            1);
        if (string.Equals(text, updated, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unable to update [project] version in '{path}'.");
        File.WriteAllText(path, updated, Utf8NoBom);
    }

    private static string ToRelativePath(string root, string path) {
        var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}