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
            !manifest.Key.Equals(evidence.Key, StringComparison.Ordinal) || manifest.Files.Length == 0)
        {
            evidence.Reason = "InvalidManifest";
            return false;
        }
        foreach (var file in manifest.Files)
        {
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
        Directory.CreateDirectory(publishDirectory);
        foreach (var file in manifest.Files)
        {
            var source = Path.Combine(entry, "payload", file.Path.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(publishDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? publishDirectory);
            File.Copy(source, target, overwrite: false);
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
        Directory.CreateDirectory(root);
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
                    return new CacheFile { Path = relative, Sha256 = ComputeSha256(path), SizeBytes = new FileInfo(path).Length };
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
        public string Key { get; set; } = string.Empty;
        public CacheFile[] Files { get; set; } = Array.Empty<CacheFile>();
    }

    private sealed class CacheFile
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
