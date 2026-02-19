using System.Globalization;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Helpers for reading/writing PowerForge engine lock files.</summary>
public static class WebEngineLockFile
{
    /// <summary>Default repository used by scaffolded websites.</summary>
    public const string DefaultRepository = "EvotecIT/PSPublishModule";
    /// <summary>Known-good stable commit used as initial lock value.</summary>
    public const string DefaultStableRef = "ab58992450def6b736a2ea87e6a492400250959f";
    /// <summary>Default release channel label.</summary>
    public const string DefaultChannel = "stable";
    /// <summary>Schema URL for lock file validation and editor hints.</summary>
    public const string DefaultSchemaUrl = "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.web.enginelock.schema.json";

    /// <summary>Create a default lock payload pinned to the current stable reference.</summary>
    public static WebEngineLockSpec CreateDefault()
    {
        return Normalize(new WebEngineLockSpec
        {
            Repository = DefaultRepository,
            Ref = DefaultStableRef,
            Channel = DefaultChannel
        }, stampUpdatedUtc: true);
    }

    /// <summary>Read and normalize an engine lock file.</summary>
    /// <param name="path">Path to the lock file.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Normalized lock payload.</returns>
    public static WebEngineLockSpec Read(string path, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Lock file path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Engine lock file not found: {fullPath}");

        var json = File.ReadAllText(fullPath);
        var parsed = JsonSerializer.Deserialize<WebEngineLockSpec>(json, options ?? WebJson.Options);
        if (parsed is null)
            throw new InvalidOperationException($"Engine lock file is invalid JSON: {fullPath}");

        return Normalize(parsed, stampUpdatedUtc: false);
    }

    /// <summary>Write an engine lock file with normalized defaults and updated UTC timestamp.</summary>
    /// <param name="path">Output lock file path.</param>
    /// <param name="spec">Lock payload to write.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    public static void Write(string path, WebEngineLockSpec spec, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Lock file path is required.", nameof(path));

        var normalized = Normalize(spec, stampUpdatedUtc: true);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var writerOptions = options is null ? new JsonSerializerOptions(WebJson.Options) : new JsonSerializerOptions(options);
        writerOptions.WriteIndented = true;
        var json = JsonSerializer.Serialize(normalized, writerOptions);
        File.WriteAllText(fullPath, json);
    }

    /// <summary>Validate required lock fields.</summary>
    /// <param name="spec">Lock payload to validate.</param>
    /// <returns>Validation issues (empty when valid).</returns>
    public static string[] Validate(WebEngineLockSpec? spec)
    {
        if (spec is null)
            return new[] { "engine lock file is empty or invalid." };

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.Repository))
            issues.Add("engine lock is missing 'repository'.");
        if (string.IsNullOrWhiteSpace(spec.Ref))
            issues.Add("engine lock is missing 'ref'.");
        return issues.ToArray();
    }

    /// <summary>Returns true when value looks like an immutable git commit SHA (40/64 hex).</summary>
    public static bool IsCommitSha(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length != 40 && trimmed.Length != 64)
            return false;

        foreach (var ch in trimmed)
        {
            var isHex = (ch >= '0' && ch <= '9') ||
                        (ch >= 'a' && ch <= 'f') ||
                        (ch >= 'A' && ch <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }

    /// <summary>Normalize lock values and optionally refresh the timestamp.</summary>
    /// <param name="spec">Lock payload to normalize.</param>
    /// <param name="stampUpdatedUtc">When true, refresh <c>updatedUtc</c> to current UTC time.</param>
    /// <returns>Normalized lock payload.</returns>
    public static WebEngineLockSpec Normalize(WebEngineLockSpec? spec, bool stampUpdatedUtc)
    {
        var normalized = spec is null ? new WebEngineLockSpec() : new WebEngineLockSpec
        {
            Schema = spec.Schema,
            Repository = spec.Repository,
            Ref = spec.Ref,
            Channel = spec.Channel,
            UpdatedUtc = spec.UpdatedUtc
        };

        if (string.IsNullOrWhiteSpace(normalized.Schema))
            normalized.Schema = DefaultSchemaUrl;
        normalized.Repository = NormalizeValue(normalized.Repository, DefaultRepository);
        normalized.Ref = NormalizeValue(normalized.Ref, string.Empty);
        normalized.Channel = NormalizeValue(normalized.Channel, DefaultChannel);

        if (stampUpdatedUtc || !IsIso8601Utc(normalized.UpdatedUtc))
            normalized.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return normalized;
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool IsIso8601Utc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
    }
}
