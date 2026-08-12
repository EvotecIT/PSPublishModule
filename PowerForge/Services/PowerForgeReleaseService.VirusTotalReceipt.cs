using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static FileStream AcquireVirusTotalReceiptLock(
        PowerForgeVirusTotalOptions options,
        string configDirectory)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("VirusTotal receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        var lockPath = receiptPath + ".lock";
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"VirusTotal receipt is already in use by another release: '{receiptPath}'.",
                exception);
        }
    }

    private static VirusTotalMonitorArtifactReceipt[] LoadVirusTotalResumeReceipts(
        PowerForgeVirusTotalOptions options,
        string configDirectory,
        string project,
        string version)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        if (!File.Exists(receiptPath))
            return Array.Empty<VirusTotalMonitorArtifactReceipt>();

        var receipt = JsonSerializer.Deserialize<VirusTotalMonitorReceiptDocument>(
            File.ReadAllText(receiptPath),
            CreateVirusTotalReceiptSerializerOptions(writeIndented: false))
            ?? throw new InvalidDataException("VirusTotal Monitor resume receipt is empty.");
        if (receipt.SchemaVersion != 1 ||
            !string.Equals(receipt.Provider, "VirusTotal Monitor", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt has an unsupported schema or provider.");
        }
        if (!string.Equals(receipt.Project, project, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt belongs to a different project.");
        }

        var completed = ValidateVirusTotalReceiptArtifacts(receipt.Artifacts);
        if (!string.Equals(receipt.Version, version, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<VirusTotalMonitorArtifactReceipt>();
        return completed;
    }

    private static VirusTotalMonitorArtifactReceipt[] ValidateVirusTotalReceiptArtifacts(
        VirusTotalMonitorArtifactReceipt[]? artifacts)
    {
        if (artifacts is null)
            throw new InvalidDataException("VirusTotal Monitor resume receipt artifacts must be an array.");

        for (var index = 0; index < artifacts.Length; index++)
        {
            var item = artifacts[index];
            if (item is null ||
                string.IsNullOrWhiteSpace(item.DestinationPath) ||
                string.IsNullOrWhiteSpace(item.MonitorId))
            {
                throw new InvalidDataException(
                    $"VirusTotal Monitor resume receipt artifact {index} must contain destinationPath and monitorId.");
            }
            if (!Enum.IsDefined(typeof(VirusTotalArtifactKind), item.Kind) ||
                !Enum.IsDefined(typeof(VirusTotalMonitorVerificationStatus), item.VerificationStatus))
            {
                throw new InvalidDataException(
                    $"VirusTotal Monitor resume receipt artifact {index} contains an unsupported enum value.");
            }
        }

        var completedGroups = artifacts
            .GroupBy(static item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conflictingDestination = completedGroups.FirstOrDefault(static group =>
            group.Select(static item => item.MonitorId).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (conflictingDestination is not null)
        {
            throw new InvalidDataException(
                $"VirusTotal Monitor receipt maps destination '{conflictingDestination.Key}' to conflicting item ids.");
        }

        return completedGroups
            .Select(static group => group.Last())
            .ToArray();
    }

    private static VirusTotalMonitorArtifactReceipt[] ApplyVirusTotalResumeReceipts(
        VirusTotalMonitorArtifactReceipt[] resumeReceipts,
        VirusTotalMonitorArtifact[] artifacts)
    {
        var completed = resumeReceipts.ToDictionary(
            static receipt => receipt.DestinationPath,
            static receipt => receipt,
            StringComparer.OrdinalIgnoreCase);
        var applicable = new List<VirusTotalMonitorArtifactReceipt>();
        foreach (var artifact in artifacts)
        {
            if (!completed.TryGetValue(artifact.DestinationPath, out var receipt))
                continue;

            artifact.ExistingItemId = receipt.MonitorId;
            applicable.Add(receipt);
        }

        return applicable.ToArray();
    }

    private static void EnsureVirusTotalReceiptWritable(
        PowerForgeVirusTotalOptions options,
        string configDirectory,
        string project)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        if (Directory.Exists(receiptPath))
        {
            throw new InvalidOperationException(
                $"VirusTotal receipt path points to an existing directory: '{receiptPath}'.");
        }
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("VirusTotal receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(receiptPath))
        {
            ValidateExistingVirusTotalReceiptIdentity(receiptPath, project);
            using var receipt = new FileStream(
                receiptPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            receipt.Flush(flushToDisk: true);
        }

        var probePath = Path.Combine(directory, $".{Path.GetFileName(receiptPath)}.{Guid.NewGuid():N}.probe");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            try { File.Delete(probePath); } catch { /* best effort */ }
        }
    }

    private static string WriteVirusTotalReceipt(
        PowerForgeVirusTotalOptions options,
        string configDirectory,
        string project,
        string version,
        VirusTotalMonitorPublishResult result)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("VirusTotal receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(
            new VirusTotalMonitorReceiptDocument
            {
                Project = project,
                Version = version,
                HashVerificationRequested = options.VerifySha256,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                Artifacts = result.Artifacts
            },
            CreateVirusTotalReceiptSerializerOptions(writeIndented: true));
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(receiptPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(receiptPath))
                File.Replace(temporaryPath, receiptPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, receiptPath);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* best effort */ }
        }

        return receiptPath;
    }

    private static JsonSerializerOptions CreateVirusTotalReceiptSerializerOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
