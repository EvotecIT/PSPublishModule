using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Shared support helpers for the project build cmdlet workflow.
/// </summary>
internal sealed class ProjectBuildSupportService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new support service.
    /// </summary>
    public ProjectBuildSupportService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads project build configuration from JSON.
    /// </summary>
    public ProjectBuildConfiguration LoadConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Config file not found.", path);

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var config = JsonSerializer.Deserialize<ProjectBuildConfiguration>(json, options);
        if (config is null)
            throw new InvalidOperationException("Config file could not be parsed.");
        return config;
    }

    /// <summary>
    /// Resolves an optional path relative to a base path.
    /// </summary>
    public static string? ResolveOptionalPath(string? value, string basePath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value!.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        return Path.GetFullPath(Path.Combine(basePath, trimmed));
    }

    /// <summary>
    /// Resolves a secret from file, environment, or inline text.
    /// </summary>
    public static string? ResolveSecret(string? inline, string? filePath, string? envName, string basePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                var resolvedPath = filePath!.Trim();
                var full = Path.IsPathRooted(resolvedPath)
                    ? resolvedPath
                    : Path.GetFullPath(Path.Combine(basePath, resolvedPath));
                if (File.Exists(full))
                    return File.ReadAllText(full).Trim();
            }
            catch
            {
                // best effort
            }
        }

        if (!string.IsNullOrWhiteSpace(envName))
        {
            try
            {
                var value = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            catch
            {
                // best effort
            }
        }

        if (!string.IsNullOrWhiteSpace(inline))
            return inline!.Trim();

        return null;
    }

    /// <summary>
    /// Maps a certificate store string to the core store enum.
    /// </summary>
    public static CertificateStoreLocation ParseCertificateStore(string? store)
    {
        if (string.IsNullOrWhiteSpace(store))
            return CertificateStoreLocation.CurrentUser;

        var trimmedStore = store!.Trim();
        return string.Equals(trimmedStore, "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? CertificateStoreLocation.LocalMachine
            : CertificateStoreLocation.CurrentUser;
    }

    /// <summary>
    /// Maps a pack strategy string to the core strategy enum.
    /// </summary>
    public static DotNetRepositoryPackStrategy ParsePackStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            return DotNetRepositoryPackStrategy.PerProject;

        var trimmedStrategy = strategy!.Trim();
        return string.Equals(trimmedStrategy, "MSBuild", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedStrategy, "Batch", StringComparison.OrdinalIgnoreCase)
            ? DotNetRepositoryPackStrategy.MSBuild
            : DotNetRepositoryPackStrategy.PerProject;
    }

    /// <summary>
    /// Converts loosely typed bound parameter values to a boolean.
    /// </summary>
    public static bool IsTrue(object? value)
    {
        if (value is null)
            return false;
        if (value is bool boolean)
            return boolean;
        if (value is int number)
            return number != 0;

        var text = value.ToString();
        return !string.IsNullOrWhiteSpace(text) && bool.TryParse(text, out var parsed) && parsed;
    }

    /// <summary>
    /// Ensures the directory exists when a path is provided.
    /// </summary>
    public static void EnsureDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Creates or cleans the staging directory.
    /// </summary>
    public void PrepareStaging(string path, bool clean)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (clean && Directory.Exists(full))
        {
            if (IsRootPath(full))
                throw new InvalidOperationException($"Refusing to clean staging root: {full}");

            _logger.Info($"Cleaning staging path: {full}");
            Directory.Delete(full, true);
        }

        Directory.CreateDirectory(full);
    }

    /// <summary>
    /// Writes the plan JSON to disk when a path is provided.
    /// </summary>
    public void TryWritePlan(DotNetRepositoryReleaseResult plan, string? path)
    {
        if (plan is null || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var trimmedPath = path!.Trim().Trim('"');
            var full = Path.GetFullPath(trimmedPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(plan, options) + Environment.NewLine;
            File.WriteAllText(full, json);
            _logger.Info($"Plan written to {full}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to write plan file: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates publish preflight requirements.
    /// </summary>
    public string? ValidatePreflight(
        bool publishNuget,
        bool publishGitHub,
        bool createReleaseZip,
        string? publishApiKey,
        string? gitHubToken,
        string? gitHubUsername,
        string? gitHubRepositoryName)
    {
        if (publishNuget && string.IsNullOrWhiteSpace(publishApiKey))
            return "PublishNuget is enabled but no PublishApiKey was resolved (use PublishApiKey, PublishApiKeyFilePath, or PublishApiKeyEnvName).";

        if (publishGitHub && !createReleaseZip)
            return "PublishGitHub is enabled but CreateReleaseZip is false.";

        if (!publishGitHub)
            return null;

        if (string.IsNullOrWhiteSpace(gitHubToken))
            return "PublishGitHub is enabled but GitHubAccessToken is missing (use GitHubAccessToken, GitHubAccessTokenFilePath, or GitHubAccessTokenEnvName).";

        if (string.IsNullOrWhiteSpace(gitHubUsername) || string.IsNullOrWhiteSpace(gitHubRepositoryName))
            return "PublishGitHub is enabled but GitHubUsername/GitHubRepositoryName are not set.";

        return null;
    }

    /// <summary>
    /// Resolves the base version token used by GitHub single-release mode.
    /// </summary>
    public static string? ResolveGitHubBaseVersion(ProjectBuildConfiguration config, DotNetRepositoryReleaseResult release)
    {
        if (!string.IsNullOrWhiteSpace(config.GitHubPrimaryProject))
        {
            var match = release.Projects.FirstOrDefault(project =>
                string.Equals(project.ProjectName, config.GitHubPrimaryProject, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.NewVersion ?? match.OldVersion;
        }

        var versions = release.Projects
            .Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.NewVersion))
            .Select(project => project.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return versions.Length == 1 ? versions[0] : null;
    }

    /// <summary>
    /// Applies a tokenized template used by project build GitHub settings.
    /// </summary>
    public static string ApplyTemplate(
        string template,
        string project,
        string version,
        string primaryProject,
        string primaryVersion,
        string repo,
        string date,
        string utcDate,
        string dateTime,
        string utcDateTime,
        string timestamp,
        string utcTimestamp)
    {
        if (string.IsNullOrWhiteSpace(template))
            return template;

        return template
            .Replace("{Project}", project ?? string.Empty)
            .Replace("{Version}", version ?? string.Empty)
            .Replace("{PrimaryProject}", primaryProject ?? string.Empty)
            .Replace("{PrimaryVersion}", primaryVersion ?? string.Empty)
            .Replace("{Repo}", repo ?? string.Empty)
            .Replace("{Repository}", repo ?? string.Empty)
            .Replace("{Date}", date ?? string.Empty)
            .Replace("{UtcDate}", utcDate ?? string.Empty)
            .Replace("{DateTime}", dateTime ?? string.Empty)
            .Replace("{UtcDateTime}", utcDateTime ?? string.Empty)
            .Replace("{Timestamp}", timestamp ?? string.Empty)
            .Replace("{UtcTimestamp}", utcTimestamp ?? string.Empty);
    }

    /// <summary>
    /// Parses GitHub tag conflict policy from configuration text.
    /// </summary>
    public static string ParseGitHubTagConflictPolicy(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "Reuse";

        return text!.Equals("AppendUtcTimestamp", StringComparison.OrdinalIgnoreCase)
            ? "AppendUtcTimestamp"
            : text.Equals("Fail", StringComparison.OrdinalIgnoreCase)
                ? "Fail"
                : "Reuse";
    }

    /// <summary>
    /// Applies the configured tag conflict policy.
    /// </summary>
    public static string ApplyTagConflictPolicy(string tag, string policy, string utcTimestampToken)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return tag;

        return string.Equals(policy, "AppendUtcTimestamp", StringComparison.OrdinalIgnoreCase)
            ? $"{tag}-{utcTimestampToken}"
            : tag;
    }

    private static bool IsRootPath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root))
            return false;

        return string.Equals(
            full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
