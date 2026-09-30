using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Packages portable Agent Plugins 1.0.0 with generated Codex and Claude compatibility files.</summary>
/// <remarks>Validates the supported JSON contract and package structure. Skill YAML semantics,
/// client support, authorization, and MCP behavior require their respective validators and runtime tests.
/// Source directories must be dedicated package roots without links or repository/runtime files.</remarks>
public sealed partial class AgentPluginPackageService
{
    private static readonly string[] CompatibilityFiles = { ".mcp.json", ".claude-plugin/plugin.json", ".codex-plugin/plugin.json", ".codex-plugin/mcp.json" };
    private static readonly Regex NamePattern = new("^(?!.*(?:--|\\.\\.))[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?\\z", RegexOptions.CultureInvariant);

    /// <summary>Validates package structure and any existing generated compatibility files without writing.</summary>
    /// <param name="sourcePath">Dedicated portable plugin root.</param>
    /// <returns>Validated identity and generated file count.</returns>
    public AgentPluginPackageResult Validate(string sourcePath) => Read(sourcePath, checkCompatibility: true).Result;

    /// <summary>Regenerates the compatibility files from portable metadata and MCP configuration.</summary>
    /// <param name="sourcePath">Dedicated portable plugin root. Only compatibility files are replaced.</param>
    /// <returns>Validated identity and generated file count.</returns>
    public AgentPluginPackageResult SyncCompatibility(string sourcePath)
    {
        var package = Read(sourcePath, checkCompatibility: false);
        foreach (var path in CompatibilityFiles)
        {
            if (!package.Generated.TryGetValue(path, out var bytes))
            {
                // A removed MCP component must not leave a stale executable legacy configuration.
                var stale = Path.Combine(package.Result.SourcePath, path);
                if (File.Exists(stale)) File.Delete(stale);
                continue;
            }
            var target = Path.Combine(package.Result.SourcePath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".powerforge-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(bytes, 0, bytes.Length);
                // Replace the directory entry instead of truncating a potentially hard-linked file.
                if (File.Exists(target)) File.Replace(temporary, target, destinationBackupFileName: null);
                else File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return package.Result;
    }

    /// <summary>Creates a versioned ZIP and SHA-256 sidecar. Existing outputs are never overwritten.</summary>
    /// <param name="sourcePath">Dedicated portable plugin root.</param>
    /// <param name="outputDirectory">Output directory outside the source tree.</param>
    /// <returns>Archive identity, location and hash.</returns>
    public AgentPluginPackageResult Pack(string sourcePath, string outputDirectory)
    {
        var package = Read(sourcePath, checkCompatibility: true);
        var output = FileSystemPathSafety.ResolveParentDirectoryAliases(outputDirectory);
        RejectLinkedAncestors(output);
        if (IsWithin(output, package.Result.SourcePath))
            throw new InvalidDataException("Package output must be outside the plugin source directory.");
        var archivePath = Path.Combine(output, package.Result.Name + "-" + package.Result.Version + ".zip");
        var checksumPath = archivePath + ".sha256";
        if (File.Exists(archivePath) || File.Exists(checksumPath))
            throw new IOException("Versioned package output already exists; choose another output directory or version.");
        Directory.CreateDirectory(output);
        var archiveCreated = false;
        var checksumCreated = false;
        try
        {
            using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                archiveCreated = true;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (var path in package.Files.Keys.Concat(package.Generated.Keys).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var destination = entry.Open();
                    if (package.Generated.TryGetValue(path, out var bytes)) destination.Write(bytes, 0, bytes.Length);
                    else
                    {
                        var source = package.Files[path];
                        RejectLinkedAncestors(source);
                        using var input = File.OpenRead(source);
                        input.CopyTo(destination);
                    }
                }
            }
            // Preserve executable package scripts on Unix; Windows builds use the regular-file default.
            var modes = new Dictionary<string, int>(StringComparer.Ordinal);
#if NET8_0_OR_GREATER
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                foreach (var pair in package.Files) modes[pair.Key] = (int)File.GetUnixFileMode(pair.Value) & 0x1FF;
#endif
            ZipArchiveUnixPermissionPatcher.ApplyUnixFilePermissions(archivePath, modes);
            using var hash = SHA256.Create();
            using var archiveInput = File.OpenRead(archivePath);
            var sha256 = BitConverter.ToString(hash.ComputeHash(archiveInput)).Replace("-", string.Empty).ToLowerInvariant();
            using (var checksum = new FileStream(checksumPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                checksumCreated = true;
                var bytes = Encoding.UTF8.GetBytes(sha256 + "  " + Path.GetFileName(archivePath) + "\n");
                checksum.Write(bytes, 0, bytes.Length);
            }
            package.Result.ArchivePath = archivePath;
            package.Result.Sha256 = sha256;
            return package.Result;
        }
        catch
        {
            if (checksumCreated) File.Delete(checksumPath);
            if (archiveCreated) File.Delete(archivePath);
            throw;
        }
    }

    private static Package Read(string sourcePath, bool checkCompatibility)
    {
        var root = FileSystemPathSafety.ResolveParentDirectoryAliases(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RejectLinkedAncestors(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (!File.Exists(Path.Combine(root, "plugin.json"))) throw new InvalidDataException("Missing root plugin.json.");
        var allFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, root, allFiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (!allFiles.ContainsKey("plugin.json")) throw new InvalidDataException("Missing root plugin.json.");
        using var manifest = ParseJson(allFiles["plugin.json"]);
        ValidateManifest(manifest.RootElement);
        var name = RequiredString(manifest.RootElement, "name");
        var version = RequiredString(manifest.RootElement, "version");
        var generated = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var mcp = allFiles.TryGetValue("mcp.json", out var mcpPath) ? ParseJson(mcpPath) : null;
        if (mcp is not null) ValidateMcp(mcp.RootElement, root);
        GenerateCompatibility(manifest.RootElement, mcp?.RootElement, generated);
        foreach (var pair in allFiles)
        {
            if (CompatibilityFiles.Contains(pair.Key, StringComparer.Ordinal)) continue;
            var top = pair.Key.Split('/')[0];
            if (!(new[] { "plugin.json", "mcp.json", "README.md", "LICENSE", "LICENSE.md", "CHANGELOG.md", "skills", "scripts", "assets", "resources", "references" }.Contains(top, StringComparer.Ordinal)
                || (Directory.Exists(Path.Combine(root, top)) && Regex.IsMatch(top, @"^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)+$"))))
                throw new InvalidDataException("Unsupported package path: " + pair.Key + ". Use a dedicated plugin source directory.");
        }
        if (Directory.Exists(Path.Combine(root, "skills")))
            foreach (var skill in Directory.GetDirectories(Path.Combine(root, "skills")))
            {
                var skillName = Path.GetFileName(skill);
                if (!Regex.IsMatch(skillName, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || skillName.Length > 64)
                    throw new InvalidDataException("Invalid skill directory name: " + skillName);
                var skillFile = Path.Combine(skill, "SKILL.md");
                if (!File.Exists(skillFile)) throw new InvalidDataException("Missing SKILL.md in " + skillName);
                if (!File.ReadAllText(skillFile).StartsWith("---", StringComparison.Ordinal))
                    throw new InvalidDataException("Missing skill frontmatter in " + skillName);
            }
        foreach (var path in CompatibilityFiles)
        {
            if (checkCompatibility && allFiles.TryGetValue(path, out var existing) &&
                (!generated.TryGetValue(path, out var expected) || !File.ReadAllBytes(existing).SequenceEqual(expected)))
                throw new InvalidDataException("Stale compatibility file: " + path + ". Run agent-plugin sync.");
            allFiles.Remove(path);
        }
        return new Package
        {
            Result = new AgentPluginPackageResult { SourcePath = root, Name = name, Version = version, FileCount = allFiles.Count + generated.Count },
            Files = allFiles.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), Generated = generated
        };
    }

    private static void Walk(string root, string directory, Dictionary<string, string> files, HashSet<string> entries)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Package links are not supported: " + path);
            var relative = path.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
            var segment = Path.GetFileName(path);
            if (segment.TrimEnd('.', ' ') != segment || segment.IndexOfAny(new[] { ':', '\\', '<', '>', '"', '|', '?', '*' }) >= 0 || segment.Any(char.IsControl)
                || Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Non-portable package path: " + relative);
            if (!entries.Add(relative)) throw new InvalidDataException("Case-colliding package paths: " + relative);
            if (segment == ".git" || segment == ".env" || segment.StartsWith(".env.", StringComparison.Ordinal))
                throw new InvalidDataException("Repository or environment file in package: " + relative);
            if (Directory.Exists(path)) Walk(root, path, files, entries);
            else
            {
                FileSystemPathSafety.RequireRegularFile(path);
                if (files.ContainsKey(relative)) throw new InvalidDataException("Case-colliding package paths: " + relative);
                files.Add(relative, path);
            }
        }
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked package/output path is not supported: " + current);
    }

    private static bool IsWithin(string path, string root)
    {
        if (path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return true;
        if (!(path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return false;
        // Admit a case-variant prefix only when the OS proves it is the same directory.
        return FileSystemPathSafety.ExistingPathComparer.Equals(path.Substring(0, root.Length), root);
    }

    private sealed class Package
    {
        internal AgentPluginPackageResult Result = null!;
        internal Dictionary<string, string> Files = null!;
        internal Dictionary<string, byte[]> Generated = null!;
    }
}
