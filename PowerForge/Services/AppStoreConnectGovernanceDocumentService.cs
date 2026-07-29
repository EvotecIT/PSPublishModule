using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Serializes and atomically persists reviewable App Store Connect governance documents.
/// </summary>
public sealed class AppStoreConnectGovernanceDocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Resolves and validates a snapshot destination before a caller performs an Apple API read.
    /// The destination is validated again at write time to retain overwrite safety if state changes.
    /// </summary>
    public string ValidateSnapshotDestination(string path, bool overwrite = false)
    {
        var fullPath = ResolvePath(path);
        EnsureSnapshotCanBeWritten(fullPath, overwrite);
        return fullPath;
    }

    /// <summary>Writes a governance snapshot, optionally replacing an existing reviewed file.</summary>
    public void WriteSnapshot(
        string path,
        AppStoreConnectGovernanceSpec snapshot,
        bool overwrite = false)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));
        var fullPath = ResolvePath(path);
        EnsureSnapshotCanBeWritten(fullPath, overwrite);
        WriteDocument(fullPath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private static void EnsureSnapshotCanBeWritten(string fullPath, bool overwrite)
    {
        if (File.Exists(fullPath) && !overwrite)
        {
            throw new InvalidOperationException(
                $"Governance snapshot already exists: {fullPath}. Enable overwrite only after preserving reviewed edits.");
        }
    }

    /// <summary>Writes a governance plan receipt using the shared atomic document contract.</summary>
    public void WritePlan(string path, AppStoreConnectGovernancePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        WriteDocument(ResolvePath(path), JsonSerializer.Serialize(plan, JsonOptions));
    }

    /// <summary>Writes a governance apply receipt using the shared atomic document contract.</summary>
    public void WriteApplyResult(string path, AppStoreConnectGovernanceApplyResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        WriteDocument(ResolvePath(path), JsonSerializer.Serialize(result, JsonOptions));
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Governance document path is required.", nameof(path));
        return Path.GetFullPath(path);
    }

    private static void WriteDocument(string path, string json)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                json + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
