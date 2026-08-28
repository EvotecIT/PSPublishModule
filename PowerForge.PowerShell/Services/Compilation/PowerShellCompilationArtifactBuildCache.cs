using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Verified content-addressed cache for generated dotnet build output.</summary>
internal static class PowerShellCompilationArtifactBuildCache
{
    internal static PowerShellCompilationBuildCacheEvidence CreateEvidence(
        PowerShellCompilationBuildSpec spec,
        string workspace,
        PowerShellCompilationTargetContract target,
        PowerShellCompilationDependencyGraph graph,
        PowerShellCompilationToolchainEvidence toolchain)
    {
        if (!spec.UseBuildCache)
            return new PowerShellCompilationBuildCacheEvidence { Reason = "DisabledByRequest" };
        var builder = new StringBuilder();
        Append(target.ContractSha256);
        Append(graph.LockSha256);
        Append(toolchain.CompilerVersion);
        Append(toolchain.CompilerSha256);
        Append(toolchain.DotNetSdkVersion);
        Append(toolchain.DotNetSdkSha256);
        Append(toolchain.BuildOperatingSystem);
        Append(toolchain.BuildArchitecture);
        Append(ComputeResolvedRestoreInputsSha256(workspace));
        foreach (var path in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetFileName(path).Equals(".powerforge-active.lock", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !IsBelow(path, Path.Combine(workspace, "publish")))
                     .Where(path => !IsBelow(path, Path.Combine(workspace, "obj")))
                     .Where(path => !IsBelow(path, Path.Combine(workspace, "bin")))
                     .OrderBy(path => Relative(workspace, path), StringComparer.Ordinal))
        {
            Append(Relative(workspace, path));
            Append(ComputeSha256(path));
        }
        using var sha = SHA256.Create();
        return new PowerShellCompilationBuildCacheEvidence
        {
            Key = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))),
            Reason = "EntryNotFound"
        };

        void Append(string value) => builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }

    internal static bool TryRestore(
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationBuildCacheEvidence evidence,
        string publishDirectory)
    {
        if (!spec.UseBuildCache || string.IsNullOrWhiteSpace(evidence.Key)) return false;
        var root = GetCacheRoot(spec);
        if (HasReparsePointInPath(root))
        {
            evidence.Reason = "UnsafeCacheRoot";
            return false;
        }
        var entry = GetEntryPath(spec, evidence.Key);
        var manifestPath = Path.Combine(entry, "cache-manifest.json");
        var completePath = Path.Combine(entry, ".complete");
        if (!File.Exists(manifestPath) || !File.Exists(completePath))
        {
            evidence.Reason = Directory.Exists(entry) ? "IncompleteEntry" : "EntryNotFound";
            return false;
        }
        if (DotNetPublishPipelineRunner.HasReparsePointBelowRoot(manifestPath, root) ||
            DotNetPublishPipelineRunner.HasReparsePointBelowRoot(completePath, root))
        {
            evidence.Reason = "UnsafeEntryPath";
            return false;
        }
        CacheManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        if (manifest is null || manifest.SchemaVersion != 1 ||
            manifest.Key is null || !manifest.Key.Equals(evidence.Key, StringComparison.Ordinal) ||
            manifest.Files is not { Length: > 0 } || manifest.Files.Any(static file => file is null))
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        var files = manifest.Files.Select(static file => file!).ToArray();
        try
        {
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.Path) ||
                    file.Path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                    string.IsNullOrWhiteSpace(file.Sha256) ||
                    file.SizeBytes < 0)
                {
                    evidence.Reason = "InvalidManifest";
                    return false;
                }
                if (!IsSafeRelativePath(file.Path))
                {
                    evidence.Reason = "UnsafeEntryPath";
                    return false;
                }
                var source = Path.GetFullPath(Path.Combine(entry, "payload", file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(source) ||
                    DotNetPublishPipelineRunner.HasReparsePointBelowRoot(source, root) ||
                    new FileInfo(source).Length != file.SizeBytes ||
                    !ComputeSha256(source).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Reason = "ContentDrift";
                    return false;
                }
            }
        }
        catch (ArgumentException)
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        catch (NotSupportedException)
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        catch (PathTooLongException)
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        if (Directory.EnumerateFileSystemEntries(publishDirectory).Any())
        {
            evidence.Reason = "PublishDirectoryNotEmpty";
            return false;
        }
        var restoreDirectory = publishDirectory + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(restoreDirectory);
            foreach (var file in files)
            {
                var source = Path.Combine(entry, "payload", file.Path.Replace('/', Path.DirectorySeparatorChar));
                var target = Path.Combine(restoreDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? restoreDirectory);
                File.Copy(source, target, overwrite: false);
                if (new FileInfo(target).Length != file.SizeBytes ||
                    !ComputeSha256(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Reason = "ContentDrift";
                    return false;
                }
            }
            Directory.Delete(publishDirectory);
            Directory.Move(restoreDirectory, publishDirectory);
        }
        catch (IOException)
        {
            evidence.Reason = "ContentDrift";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            evidence.Reason = "ContentDrift";
            return false;
        }
        finally
        {
            if (Directory.Exists(restoreDirectory)) Directory.Delete(restoreDirectory, recursive: true);
        }
        evidence.Hit = true;
        evidence.Reason = "VerifiedContentAddressedHit";
        return true;
    }

    internal static void Store(
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationBuildCacheEvidence evidence,
        string publishDirectory)
    {
        if (!spec.UseBuildCache || evidence.Hit || string.IsNullOrWhiteSpace(evidence.Key)) return;
        var entry = GetEntryPath(spec, evidence.Key);
        var parent = Path.GetDirectoryName(entry) ?? throw new InvalidOperationException("Compilation cache entry has no parent.");
        var root = GetCacheRoot(spec);
        if (HasReparsePointInPath(root))
        {
            evidence.Reason = "UnsafeCacheRoot";
            return;
        }
        Directory.CreateDirectory(root);
        if (HasReparsePointInPath(root))
        {
            evidence.Reason = "UnsafeCacheRoot";
            return;
        }
        if (Directory.Exists(parent) && DotNetPublishPipelineRunner.HasReparsePointBelowRoot(parent, root))
        {
            evidence.Reason = "UnsafeCacheRoot";
            return;
        }
        Directory.CreateDirectory(parent);
        if (DotNetPublishPipelineRunner.HasReparsePointBelowRoot(parent, root))
        {
            evidence.Reason = "UnsafeCacheRoot";
            return;
        }
        if (Directory.Exists(entry))
        {
            evidence.Reason = "ExistingEntryUnavailable";
            return;
        }
        var temporary = entry + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var payload = Path.Combine(temporary, "payload");
            Directory.CreateDirectory(payload);
            var files = Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(path => Relative(publishDirectory, path), StringComparer.Ordinal)
                .Select(path =>
                {
                    var relative = Relative(publishDirectory, path);
                    var target = Path.Combine(payload, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? payload);
                    File.Copy(path, target, overwrite: false);
                    return new CacheFile { Path = relative, Sha256 = ComputeSha256(target), SizeBytes = new FileInfo(target).Length };
                }).ToArray();
            if (files.Length == 0) throw new InvalidOperationException("Generated build produced no files to cache.");
            File.WriteAllText(
                Path.Combine(temporary, "cache-manifest.json"),
                JsonSerializer.Serialize(new CacheManifest { Key = evidence.Key, Files = files }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temporary, ".complete"), evidence.Key, new UTF8Encoding(false));
            try { Directory.Move(temporary, entry); }
            catch (IOException) when (Directory.Exists(entry)) { Directory.Delete(temporary, recursive: true); }
            evidence.Reason = "StoredAfterMiss";
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static string GetEntryPath(PowerShellCompilationBuildSpec spec, string key)
    {
        var root = GetCacheRoot(spec);
        var entry = Path.GetFullPath(Path.Combine(root, key.Substring(0, 2), key));
        PowerShellCompilationPathSafety.EnsureContained(root, entry, "Compilation cache key escaped its configured root.");
        return entry;
    }

    private static string GetCacheRoot(PowerShellCompilationBuildSpec spec)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(spec.BuildCacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerForge", "CompilationCache", "v1")
            : spec.BuildCacheDirectory!.Trim().Trim('"'));

    private static bool IsSafeRelativePath(string path)
        => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
           !path.Split('/', '\\').Any(static part => part is "" or "." or "..");

    private static bool HasReparsePointInPath(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current.Parent;
        }
        return false;
    }

    internal static string ComputeResolvedRestoreInputsSha256(string workspace)
    {
        var assetsPath = Path.Combine(workspace, "obj", "project.assets.json");
        if (!File.Exists(assetsPath)) return "NoResolvedRestoreInputs";
        var builder = new StringBuilder();
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        Append("project.assets.json");
        Append(ComputeNormalizedAssetsSha256(document.RootElement, workspace));
        if (!document.RootElement.TryGetProperty("packageFolders", out var packageFolders) ||
            packageFolders.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Generated restore assets do not contain packageFolders and libraries objects.");
        var roots = packageFolders.EnumerateObject().Select(static property => property.Name).ToArray();
        foreach (var library in libraries.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (!library.Value.TryGetProperty("type", out var type) || !"package".Equals(type.GetString(), StringComparison.OrdinalIgnoreCase) ||
                !library.Value.TryGetProperty("path", out var pathValue))
                continue;
            var packagePath = pathValue.GetString();
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new InvalidDataException($"Resolved package '{library.Name}' has no package path.");
            var resolvedPackagePath = packagePath!;
            var matches = roots.Select(root => Path.GetFullPath(Path.Combine(root, resolvedPackagePath.Replace('/', Path.DirectorySeparatorChar))))
                .Where(Directory.Exists)
                .ToArray();
            if (matches.Length == 0)
                throw new InvalidDataException($"Resolved package '{library.Name}' is absent from every restore package folder.");
            Append(library.Name);
            foreach (var packageRoot in matches)
            foreach (var file in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => Relative(packageRoot, path), StringComparer.Ordinal))
            {
                Append(Relative(packageRoot, file));
                Append(ComputeSha256(file));
            }
        }
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));

        void Append(string value) => builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }

    private static string ComputeNormalizedAssetsSha256(JsonElement root, string workspace)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteNormalizedJson(writer, root, Path.GetFullPath(workspace));
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream.ToArray()));
    }

    private static void WriteNormalizedJson(Utf8JsonWriter writer, JsonElement element, string workspace)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteNormalizedJson(writer, property.Value, workspace);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteNormalizedJson(writer, item, workspace);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                value = ReplacePath(value, workspace, "$WORKSPACE");
                value = ReplacePath(value, workspace.Replace('\\', '/'), "$WORKSPACE");
                writer.WriteStringValue(value);
                break;
            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }

    private static string ReplacePath(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = value.Substring(0, index) + newValue + value.Substring(index + oldValue.Length);
            index = value.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    private static bool IsBelow(string path, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return PowerShellCompilationPathSafety.PathStartsWith(Path.GetFullPath(path), prefix);
    }

    private static string Relative(string root, string path)
        => FrameworkCompatibility.GetRelativePath(root, path).Replace('\\', '/');

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }

    private static string Hex(IEnumerable<byte> bytes)
        => string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

    private sealed class CacheManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string? Key { get; set; }
        public CacheFile?[]? Files { get; set; }
    }

    private sealed class CacheFile
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
