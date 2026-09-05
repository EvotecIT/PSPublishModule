using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Loads, validates, normalizes, and scaffolds the single portable PowerShell compilation project model.</summary>
public sealed class PowerShellCompilationProjectManifestService
{
    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>Creates a one-target project manifest without inspecting or executing the source.</summary>
    public PowerShellCompilationProjectManifest Create(
        string projectPath,
        string sourcePath,
        string projectName,
        PowerShellCompilationTargetContract target)
    {
        var fullProjectPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        var projectRoot = Path.GetDirectoryName(fullProjectPath) ?? Directory.GetCurrentDirectory();
        var fullSourcePath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(fullProjectPath, "Project manifest path traverses a symbolic link or junction.");
        PowerShellCompilationPathSafety.EnsureContained(projectRoot, fullSourcePath, "Project source must be contained by the project directory.");
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(fullSourcePath, "Project source traverses a symbolic link or junction.");
        if (!File.Exists(fullSourcePath) && !Directory.Exists(fullSourcePath))
            throw new FileNotFoundException("PowerShell compilation project source was not found.", fullSourcePath);
        var normalizedTarget = PowerShellCompilationTargetContractService.Normalize(target);
        var targetName = CreateTargetName(normalizedTarget);
        var manifest = new PowerShellCompilationProjectManifest
        {
            Name = NormalizeName(projectName, nameof(projectName)),
            SemanticProfileId = normalizedTarget.SemanticProfileId,
            Sources = new[] { NormalizeRelative(projectRoot, fullSourcePath) },
            Artifacts = new[]
            {
                new PowerShellCompilationProjectArtifact
                {
                    Name = targetName,
                    Target = normalizedTarget,
                    OutputDirectory = $"artifacts/{targetName}",
                    DependencyLock = $".powerforge/locks/{targetName}.lock.json"
                }
            }
        };
        return Normalize(fullProjectPath, manifest, requireInputs: true);
    }

    /// <summary>Loads and validates a project manifest.</summary>
    public PowerShellCompilationProjectManifest Load(string projectPath, bool requireInputs = true)
    {
        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("PowerShell compilation project was not found.", fullPath);
        var manifest = JsonSerializer.Deserialize<PowerShellCompilationProjectManifest>(File.ReadAllText(fullPath), JsonOptions)
                       ?? throw new InvalidDataException($"PowerShell compilation project '{fullPath}' is empty.");
        return Normalize(fullPath, manifest, requireInputs);
    }

    /// <summary>Writes a normalized project manifest with portable relative paths.</summary>
    public void Save(string projectPath, PowerShellCompilationProjectManifest manifest)
    {
        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(fullPath, "Project manifest path traverses a symbolic link or junction.");
        var normalized = Normalize(fullPath, manifest, requireInputs: true);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    internal static ProjectContext Open(string projectPath, bool requireInputs = true)
    {
        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        var manifest = new PowerShellCompilationProjectManifestService().Load(fullPath, requireInputs);
        return new ProjectContext(fullPath, manifest);
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return ToHex(algorithm.ComputeHash(stream));
    }

    private static PowerShellCompilationProjectManifest Normalize(
        string projectPath,
        PowerShellCompilationProjectManifest manifest,
        bool requireInputs)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported PowerShell compilation project schema {manifest.SchemaVersion}.");
        manifest.Name = NormalizeName(manifest.Name, "project name");
        manifest.SemanticProfileId = manifest.SemanticProfileId?.Trim() ?? string.Empty;
        _ = PowerShellCompilationSemanticOracleCatalog.Get(manifest.SemanticProfileId);
        var root = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Directory.GetCurrentDirectory();
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(projectPath, "Project manifest path traverses a symbolic link or junction.");
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(root, "Project root traverses a symbolic link or junction.");
        manifest.Sources = NormalizeRelativePaths(root, manifest.Sources, requireInputs, "source");
        if (manifest.Sources.Length == 0) throw new InvalidDataException("A PowerShell compilation project requires at least one source.");
        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
            manifest.EntryPoint = NormalizeRelativePath(root, manifest.EntryPoint!, requireInputs, "entrypoint");
        manifest.Resources ??= new PowerShellCompilationProjectResourcePolicy();
        if (!Enum.IsDefined(typeof(PowerShellCompilationResourceMode), manifest.Resources.Mode))
            throw new InvalidDataException("The project resource mode is invalid.");
        manifest.Resources.Include ??= Array.Empty<string>();
        manifest.Resources.Exclude ??= Array.Empty<string>();
        manifest.ProviderPackages = NormalizeRelativePaths(root, manifest.ProviderPackages, requireInputs, "provider package");
        manifest.ProviderTrust ??= new PowerShellCompilationProviderTrustPolicy();
        manifest.Diagnostics ??= new PowerShellCompilationDiagnosticsPolicy();
        manifest.Artifacts ??= Array.Empty<PowerShellCompilationProjectArtifact>();
        if (manifest.Artifacts.Length == 0) throw new InvalidDataException("A PowerShell compilation project requires at least one artifact target.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            artifact.Name = NormalizeName(artifact.Name, "artifact target name");
            if (!names.Add(artifact.Name)) throw new InvalidDataException($"Duplicate project target name '{artifact.Name}'.");
            var target = artifact.Target ?? throw new InvalidDataException($"Project target '{artifact.Name}' has no target contract.");
            artifact.Target = PowerShellCompilationTargetContractService.Normalize(
                target,
                target.SchemaVersion < 3 ? manifest.SemanticProfileId : null);
            if (!artifact.Target.SemanticProfileId.Equals(manifest.SemanticProfileId, StringComparison.Ordinal))
                throw new InvalidDataException($"Project target '{artifact.Name}' semantic profile '{artifact.Target.SemanticProfileId}' differs from project profile '{manifest.SemanticProfileId}'.");
            PowerShellCompilationBuildSpec.EnsureModeSupported(artifact.Target.ArtifactKind, artifact.Target.Mode);
            if (!identities.Add(artifact.Target.ContractSha256)) throw new InvalidDataException($"Project target '{artifact.Name}' duplicates another exact artifact variant.");
            artifact.OutputDirectory = NormalizeRelativePath(root, artifact.OutputDirectory, requireExists: false, "output directory");
            artifact.DependencyLock = NormalizeRelativePath(root, artifact.DependencyLock, requireExists: false, "dependency lock");
            if (!string.IsNullOrWhiteSpace(artifact.ProviderLock))
                artifact.ProviderLock = NormalizeRelativePath(root, artifact.ProviderLock!, requireExists: false, "provider lock");
            if (!string.IsNullOrWhiteSpace(artifact.ExpectedAbiSha256) && !IsSha256(artifact.ExpectedAbiSha256!))
                throw new InvalidDataException($"Project target '{artifact.Name}' has an invalid ABI SHA-256.");
        }
        return manifest;
    }

    private static string[] NormalizeRelativePaths(string root, IEnumerable<string>? values, bool requireExists, string label)
        => (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeRelativePath(root, value, requireExists, label))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();

    private static string NormalizeRelativePath(string root, string value, bool requireExists, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Project {label} path is required.");
        var trimmed = value.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed) || LooksLikeWindowsRootedPath(trimmed))
            throw new InvalidDataException($"Project {label} path '{value}' must be relative.");
        var full = Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        PowerShellCompilationPathSafety.EnsureContained(root, full, $"Project {label} path '{value}' escapes the project root.");
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(full, $"Project {label} path '{value}' traverses a symbolic link or junction.");
        if (requireExists && !File.Exists(full) && !Directory.Exists(full))
            throw new FileNotFoundException($"Project {label} path was not found.", full);
        return NormalizeRelative(root, full);
    }

    private static string NormalizeRelative(string root, string fullPath)
        => FrameworkCompatibility.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static string NormalizeName(string value, string label)
    {
        var name = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException($"The {label} is not a portable identifier.");
        return name;
    }

    private static string CreateTargetName(PowerShellCompilationTargetContract target)
    {
        var rid = string.IsNullOrWhiteSpace(target.RuntimeIdentifier) ? "portable" : target.RuntimeIdentifier;
        return $"{target.ArtifactKind}-{target.Mode}-{target.TargetFramework}-{rid}-{target.Deployment}".ToLowerInvariant();
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    internal static string ToHex(IEnumerable<byte> bytes)
        => string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

    private static bool LooksLikeWindowsRootedPath(string value)
        => value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    internal sealed class ProjectContext
    {
        internal ProjectContext(string projectPath, PowerShellCompilationProjectManifest manifest)
        {
            ProjectPath = projectPath;
            Root = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
            Manifest = manifest;
        }

        internal string ProjectPath { get; }
        internal string Root { get; }
        internal PowerShellCompilationProjectManifest Manifest { get; }
        internal string Resolve(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
            PowerShellCompilationPathSafety.EnsureContained(Root, path, $"Project path '{relative}' escapes the project root.");
            PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(path, $"Project path '{relative}' traverses a symbolic link or junction.");
            return path;
        }
        internal string[] Sources => Manifest.Sources.Select(Resolve).ToArray();
        internal string? EntryPoint => string.IsNullOrWhiteSpace(Manifest.EntryPoint) ? null : Resolve(Manifest.EntryPoint!);
    }
}
