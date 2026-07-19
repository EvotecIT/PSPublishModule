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
        ValidateAssetFileName(hacsFileName);
        var zipRelease = ReadOptionalBoolean(hacs, "zip_release");
        var packageJsonPath = Path.Combine(root, "package.json");
        var customComponents = Path.Combine(root, "custom_components");
        if (!Directory.Exists(customComponents)) {
            if (zipRelease)
                throw new InvalidOperationException("hacs.json zip_release is only valid for a Home Assistant custom integration under custom_components.");
            if (string.IsNullOrWhiteSpace(hacsFileName) || !File.Exists(packageJsonPath))
                throw new InvalidOperationException("The repository is neither a HACS Lovelace plugin nor a Home Assistant custom integration.");

            var packageLockPath = Path.Combine(root, "package-lock.json");
            if (!File.Exists(packageLockPath))
                throw new InvalidOperationException("HACS Lovelace plugin releases require package-lock.json so npm ci is reproducible.");

            var package = ReadJsonObject(packageJsonPath);
            var version = ReadRequiredString(package, "version", packageJsonPath);
            ValidatePackageLockVersion(packageLockPath, version);
            return new HomeAssistantRepositorySnapshot {
                Kind = HomeAssistantRepositoryKind.LovelacePlugin,
                Version = version,
                PackageJsonPath = packageJsonPath,
                PackageLockPath = packageLockPath,
                HacsPath = hacsPath,
                HacsFileName = hacsFileName,
                ZipRelease = false
            };
        }

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
            UpdatePackageLockVersion(snapshot.PackageLockPath!, version);
            changed.Add(ToRelativePath(repositoryRoot, snapshot.PackageLockPath!));
        } else {
            throw new InvalidOperationException("The repository layout must be resolved before updating its version.");
        }

        return changed;
    }

    internal IReadOnlyList<string> GetVersionMetadataFiles(HomeAssistantRepositorySnapshot snapshot, string repositoryRoot) {
        if (snapshot.Kind == HomeAssistantRepositoryKind.Integration) {
            var files = new List<string> { ToRelativePath(repositoryRoot, snapshot.ManifestPath!) };
            if (!string.IsNullOrWhiteSpace(snapshot.PyProjectPath))
                files.Add(ToRelativePath(repositoryRoot, snapshot.PyProjectPath!));
            return files;
        }

        if (snapshot.Kind == HomeAssistantRepositoryKind.LovelacePlugin) {
            return new[] {
                ToRelativePath(repositoryRoot, snapshot.PackageJsonPath!),
                ToRelativePath(repositoryRoot, snapshot.PackageLockPath!)
            };
        }

        throw new InvalidOperationException("The repository layout must be resolved before listing version metadata files.");
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
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = null,
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = null,
            ["ACTIONS_RUNTIME_TOKEN"] = null,
            ["GH_TOKEN"] = null,
            ["GITHUB_ENV"] = null,
            ["GITHUB_OUTPUT"] = null,
            ["GITHUB_PATH"] = null,
            ["GITHUB_STEP_SUMMARY"] = null,
            ["GITHUB_TOKEN"] = null,
            ["GIT_ASKPASS"] = null,
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_GLOBAL"] = Path.DirectorySeparatorChar == '\\' ? "NUL" : "/dev/null",
            ["GIT_CONFIG_KEY_0"] = "credential.helper",
            ["GIT_CONFIG_KEY_1"] = "http.extraheader",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_VALUE_0"] = string.Empty,
            ["GIT_CONFIG_VALUE_1"] = string.Empty,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["INPUT_GITHUB_TOKEN"] = null,
            ["SSH_AUTH_SOCK"] = null
        };
        var result = _processRunner.RunAsync(
                new ProcessRunRequest(executable, workingDirectory, arguments, TimeSpan.FromMinutes(15), environment))
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

    private static void ValidateAssetFileName(string? fileName) {
        if (fileName is null) return;
        if (Path.IsPathRooted(fileName) ||
            fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0 ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)) {
            throw new InvalidOperationException("hacs.json filename must be a file name without a directory or rooted path.");
        }
    }

    private static void UpdateJsonVersion(string path, string version) {
        UpdateJsonStringValues(path, version, new[] { "version" });
    }

    private static void UpdatePackageLockVersion(string path, string version) {
        UpdateJsonStringValues(
            path,
            version,
            new[] { "version" },
            new[] { "packages", string.Empty, "version" });
    }

    private static void ValidatePackageLockVersion(string path, string expectedVersion) {
        var value = ReadJsonObject(path);
        var lockVersion = ReadRequiredString(value, "version", path);
        if (value["packages"] is not JsonObject packages || packages[""] is not JsonObject rootPackage)
            throw new InvalidOperationException($"'{path}' does not contain packages[''] metadata.");
        var rootVersion = ReadRequiredString(rootPackage, "version", $"{path} packages['']");
        if (!string.Equals(lockVersion, expectedVersion, StringComparison.Ordinal) ||
            !string.Equals(rootVersion, expectedVersion, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Version drift detected: package.json has {expectedVersion}, while package-lock.json has {lockVersion} and packages[''] has {rootVersion}.");
        }
    }

    private static void UpdateJsonStringValues(string path, string value, params string[][] targetPaths) {
        var bytes = File.ReadAllBytes(path);
        var bomLength = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(bytes, bomLength, bytes.Length - bomLength));
        var containerPath = new List<string>();
        var containerSegments = new Stack<bool>();
        var replacements = new List<(int Start, int End)>();
        var updatedPaths = new bool[targetPaths.Length];
        string? pendingProperty = null;

        while (reader.Read()) {
            switch (reader.TokenType) {
                case JsonTokenType.PropertyName:
                    pendingProperty = reader.GetString()
                                      ?? throw new InvalidOperationException($"'{path}' contains a null JSON property name.");
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    var pushedSegment = pendingProperty is not null;
                    if (pushedSegment) containerPath.Add(pendingProperty!);
                    containerSegments.Push(pushedSegment);
                    pendingProperty = null;
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (containerSegments.Count == 0)
                        throw new InvalidOperationException($"'{path}' contains unbalanced JSON containers.");
                    if (containerSegments.Pop()) containerPath.RemoveAt(containerPath.Count - 1);
                    pendingProperty = null;
                    break;
                case JsonTokenType.String:
                    if (pendingProperty is not null) {
                        var targetIndex = FindTargetPath(containerPath, pendingProperty, targetPaths);
                        if (targetIndex >= 0) {
                            if (updatedPaths[targetIndex])
                                throw new InvalidOperationException($"'{path}' contains duplicate JSON metadata at '{string.Join("/", targetPaths[targetIndex])}'.");
                            updatedPaths[targetIndex] = true;
                            replacements.Add((
                                checked(bomLength + (int)reader.TokenStartIndex),
                                checked(bomLength + (int)reader.BytesConsumed)));
                        }
                    }
                    pendingProperty = null;
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    pendingProperty = null;
                    break;
            }
        }

        for (var i = 0; i < updatedPaths.Length; i++) {
            if (!updatedPaths[i])
                throw new InvalidOperationException($"'{path}' does not contain string JSON metadata at '{string.Join("/", targetPaths[i])}'.");
        }

        var replacement = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        using var output = new MemoryStream(bytes.Length + replacements.Count * replacement.Length);
        var cursor = 0;
        foreach (var range in replacements) {
            output.Write(bytes, cursor, range.Start - cursor);
            output.Write(replacement, 0, replacement.Length);
            cursor = range.End;
        }
        output.Write(bytes, cursor, bytes.Length - cursor);
        File.WriteAllBytes(path, output.ToArray());
    }

    private static int FindTargetPath(IReadOnlyList<string> containerPath, string propertyName, IReadOnlyList<string[]> targetPaths) {
        for (var targetIndex = 0; targetIndex < targetPaths.Count; targetIndex++) {
            var targetPath = targetPaths[targetIndex];
            if (targetPath.Length != containerPath.Count + 1 ||
                !string.Equals(targetPath[targetPath.Length - 1], propertyName, StringComparison.Ordinal)) {
                continue;
            }

            var matches = true;
            for (var segmentIndex = 0; segmentIndex < containerPath.Count; segmentIndex++) {
                if (string.Equals(targetPath[segmentIndex], containerPath[segmentIndex], StringComparison.Ordinal)) continue;
                matches = false;
                break;
            }
            if (matches) return targetIndex;
        }

        return -1;
    }

    private static string ReadPyProjectVersion(string path) {
        var text = File.ReadAllText(path);
        var span = FindPyProjectVersion(text, path);
        return text.Substring(span.Start, span.Length).Trim();
    }

    private static void UpdatePyProjectVersion(string path, string version) {
        var text = File.ReadAllText(path);
        var span = FindPyProjectVersion(text, path);
        var updated = text.Substring(0, span.Start) + version + text.Substring(span.Start + span.Length);
        File.WriteAllText(path, updated, Utf8NoBom);
    }

    private static (int Start, int Length) FindPyProjectVersion(string text, string path) {
        var projectHeader = Regex.Match(text, @"(?m)^[ \t]*\[project\][ \t]*(?:#.*)?\r?$");
        if (!projectHeader.Success)
            throw new InvalidOperationException($"'{path}' does not define a [project] table.");

        var sectionStart = projectHeader.Index + projectHeader.Length;
        var nextTable = Regex.Match(
            text.Substring(sectionStart),
            @"(?m)^[ \t]*\[\[?[^\r\n\]]+\]\]?[ \t]*(?:#.*)?\r?$");
        var sectionEnd = nextTable.Success ? sectionStart + nextTable.Index : text.Length;
        var section = text.Substring(sectionStart, sectionEnd - sectionStart);
        var versions = Regex.Matches(
            section,
            @"(?m)^[ \t]*version[ \t]*=[ \t]*""(?<version>[^""\r\n]+)""[ \t]*(?:#.*)?\r?$");
        if (versions.Count != 1)
            throw new InvalidOperationException($"'{path}' must define exactly one version in the [project] table, but found {versions.Count}.");

        var group = versions[0].Groups["version"];
        return (sectionStart + group.Index, group.Length);
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
