using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Persists immutable Apple release attempts while maintaining an atomic latest-state receipt.
/// </summary>
internal sealed class AppleReleaseReceiptStore
{
    private const long MaximumReceiptBytes = 2L * 1024L * 1024L;
    private static readonly StringComparison PathComparison =
        Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private readonly Func<DateTimeOffset> _utcNow;

    internal AppleReleaseReceiptStore(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Reads the atomic latest receipt and every immutable history entry, rejecting malformed evidence.
    /// </summary>
    internal PowerForgeAppleReleaseReceipt[] ReadAll(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var receipts = new List<PowerForgeAppleReleaseReceipt>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(plan.ReceiptPath))
            AddReceipt(plan, plan.ReceiptPath, receipts, identities);

        if (Directory.Exists(plan.ReceiptHistoryPath))
        {
            EnsureUnlinkedPath(plan.ProjectRoot, plan.ReceiptHistoryPath, "Apple receipt history");
            foreach (var entry in Directory.EnumerateFileSystemEntries(plan.ReceiptHistoryPath)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                if (!File.Exists(entry) ||
                    !Path.GetExtension(entry).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Apple receipt history contains an unsupported entry: {entry}");
                }

                AddReceipt(plan, entry, receipts, identities);
            }
        }

        ValidateReceiptChain(plan, receipts);
        return OrderReceipts(receipts);
    }

    /// <summary>Validates every configured receipt path and the complete evidence chain without writing.</summary>
    internal void Validate(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        EnsureSafeOutputPath(plan.ProjectRoot, plan.ReceiptPath, "AppleApps.Automation.ReceiptPath");
        EnsureSafeOutputPath(plan.ProjectRoot, plan.ReceiptHistoryPath, "AppleApps.Automation.ReceiptHistoryPath");
        _ = ReadAll(plan);
    }

    /// <summary>
    /// Writes one immutable attempt receipt and atomically updates the configured latest receipt.
    /// </summary>
    internal void WriteAttempt(PowerForgeAppleReleasePlan plan, PowerForgeAppleReleaseReceipt receipt)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (receipt is null)
            throw new ArgumentNullException(nameof(receipt));

        EnsureSafeOutputPath(plan.ProjectRoot, plan.ReceiptPath, "AppleApps.Automation.ReceiptPath");
        EnsureSafeOutputPath(plan.ProjectRoot, plan.ReceiptHistoryPath, "AppleApps.Automation.ReceiptHistoryPath");
        var previousReceipt = ReadAll(plan).FirstOrDefault();

        var suppliedAttemptId = receipt.AttemptId;
        var normalizedAttemptId = string.IsNullOrWhiteSpace(suppliedAttemptId)
            ? Guid.NewGuid().ToString("N")
            : suppliedAttemptId!.Trim().ToLowerInvariant();
        if (normalizedAttemptId.Length != 32 || normalizedAttemptId.Any(static value => !Uri.IsHexDigit(value)))
            throw new InvalidOperationException("Apple receipt attempt id must contain exactly 32 hexadecimal characters.");
        receipt.AttemptId = normalizedAttemptId;

        if (receipt.CheckedAt == default)
            receipt.CheckedAt = _utcNow();

        var historyDirectory = Path.GetFullPath(plan.ReceiptHistoryPath);
        Directory.CreateDirectory(historyDirectory);
        EnsureUnlinkedPath(plan.ProjectRoot, historyDirectory, "Apple receipt history");
        PreserveLegacyLatest(plan, previousReceipt, historyDirectory);

        var safeAction = receipt.Action.ToString().ToLowerInvariant();
        var timestamp = receipt.CheckedAt.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        var historyPath = Path.Combine(historyDirectory, $"{timestamp}-{safeAction}-{receipt.AttemptId}.json");
        receipt.ReceiptPath = ToRelativePath(plan.ProjectRoot, plan.ReceiptPath);
        receipt.HistoryPath = ToRelativePath(plan.ProjectRoot, historyPath);
        receipt.PreviousReceiptSha256 = previousReceipt?.ReceiptSha256;
        receipt.ReceiptSha256 = ComputeReceiptSha256(receipt);

        var payload = Serialize(receipt);
        WriteImmutableHistoryEntry(historyDirectory, historyPath, payload);

        WriteLatest(plan.ProjectRoot, plan.ReceiptPath, payload, "AppleApps.Automation.ReceiptPath");
    }

    /// <summary>Atomically writes a non-journaled Apple plan receipt.</summary>
    internal void WritePlan(string projectRoot, string path, PowerForgeAppleReleaseReceipt receipt)
    {
        if (receipt is null)
            throw new ArgumentNullException(nameof(receipt));
        WriteLatest(projectRoot, path, Serialize(receipt), "AppleApps.Automation.PlanReceiptPath");
    }

    /// <summary>Computes the canonical SHA-256 stored inside an immutable receipt.</summary>
    internal static string ComputeReceiptSha256(PowerForgeAppleReleaseReceipt receipt)
    {
        if (receipt is null)
            throw new ArgumentNullException(nameof(receipt));

        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(receipt, CreateOptions(writeIndented: false)));
        return ComputeReceiptSha256(document.RootElement);
    }

    /// <summary>
    /// Computes the receipt hash from the represented JSON rather than the current CLR model. This keeps
    /// historical hashes valid when a future PowerForge version adds optional receipt properties.
    /// </summary>
    internal static string ComputeReceiptSha256Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ComputeReceiptSha256(document.RootElement);
    }

    private static void AddReceipt(
        PowerForgeAppleReleasePlan plan,
        string path,
        ICollection<PowerForgeAppleReleaseReceipt> receipts,
        ISet<string> identities)
    {
        var receipt = Read(plan.ProjectRoot, path);
        var identity = !string.IsNullOrWhiteSpace(receipt.ReceiptSha256)
            ? receipt.ReceiptSha256!
            : $"legacy:{receipt.CheckedAt:O}:{receipt.Action}:{receipt.SourceCommit}";
        if (identities.Add(identity))
            receipts.Add(receipt);
    }

    private static void ValidateReceiptChain(
        PowerForgeAppleReleasePlan plan,
        IReadOnlyCollection<PowerForgeAppleReleaseReceipt> receipts)
    {
        var hashes = receipts
            .Where(static receipt => !string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
            .Select(static receipt => receipt.ReceiptSha256!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attemptHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previousHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootCount = 0;
        foreach (var receipt in receipts)
        {
            if (string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
                continue;
            if (string.IsNullOrWhiteSpace(receipt.AttemptId) ||
                receipt.AttemptId!.Length != 32 ||
                receipt.AttemptId.Any(static value => !Uri.IsHexDigit(value)))
            {
                throw new InvalidOperationException("Apple release receipt has an invalid attempt id.");
            }
            if (attemptHashes.TryGetValue(receipt.AttemptId, out var existingHash) &&
                !existingHash.Equals(receipt.ReceiptSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Apple release receipt attempt '{receipt.AttemptId}' has conflicting evidence.");
            }

            attemptHashes[receipt.AttemptId] = receipt.ReceiptSha256!;
            ValidateHistoryBinding(plan, receipt);
            if (string.IsNullOrWhiteSpace(receipt.PreviousReceiptSha256))
            {
                rootCount++;
                continue;
            }
            if (!IsSha256(receipt.PreviousReceiptSha256) ||
                receipt.PreviousReceiptSha256!.Equals(receipt.ReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
                !hashes.Contains(receipt.PreviousReceiptSha256) ||
                !previousHashes.Add(receipt.PreviousReceiptSha256))
            {
                throw new InvalidOperationException(
                    $"Apple release receipt attempt '{receipt.AttemptId}' has a broken previous-receipt chain.");
            }
        }

        if (attemptHashes.Count > 0 && rootCount != 1)
        {
            throw new InvalidOperationException(
                "Apple release receipt history must contain exactly one complete evidence chain.");
        }
    }

    private static void ValidateHistoryBinding(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleReleaseReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.HistoryPath))
            throw new InvalidOperationException("Immutable Apple release receipt is missing its history path.");

        var historyRoot = Path.GetFullPath(plan.ReceiptHistoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var historyPath = Path.GetFullPath(Path.Combine(plan.ProjectRoot, receipt.HistoryPath!));
        if (!IsWithinRoot(historyPath, historyRoot) || !File.Exists(historyPath))
        {
            throw new InvalidOperationException(
                $"Immutable Apple release receipt history entry is missing or outside the configured history directory: {receipt.HistoryPath}");
        }

        EnsureUnlinkedPath(plan.ProjectRoot, historyPath, "Apple release receipt history entry");
        var historyReceipt = Read(plan.ProjectRoot, historyPath);
        if (!string.Equals(historyReceipt.ReceiptSha256, receipt.ReceiptSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Immutable Apple release receipt history entry does not contain its declared receipt: {receipt.HistoryPath}");
        }
    }

    private static PowerForgeAppleReleaseReceipt[] OrderReceipts(
        IReadOnlyCollection<PowerForgeAppleReleaseReceipt> receipts)
    {
        var hashed = receipts
            .Where(static receipt => !string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
            .ToDictionary(static receipt => receipt.ReceiptSha256!, StringComparer.OrdinalIgnoreCase);
        var referenced = hashed.Values
            .Where(static receipt => !string.IsNullOrWhiteSpace(receipt.PreviousReceiptSha256))
            .Select(static receipt => receipt.PreviousReceiptSha256!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PowerForgeAppleReleaseReceipt>();
        if (hashed.Count > 0)
        {
            var tail = hashed.Values.Single(receipt => !referenced.Contains(receipt.ReceiptSha256!));
            var current = tail;
            while (true)
            {
                ordered.Add(current);
                if (string.IsNullOrWhiteSpace(current.PreviousReceiptSha256))
                    break;
                current = hashed[current.PreviousReceiptSha256!];
            }

            if (ordered.Count != hashed.Count)
                throw new InvalidOperationException("Apple release receipt history contains a disconnected evidence chain.");
        }

        ordered.AddRange(receipts
            .Where(static receipt => string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
            .OrderByDescending(static receipt => receipt.CheckedAt));
        return ordered.ToArray();
    }

    private static PowerForgeAppleReleaseReceipt Read(string projectRoot, string path)
    {
        EnsureUnlinkedPath(projectRoot, path, "Apple release receipt");
        var file = new FileInfo(path);
        if (file.Length > MaximumReceiptBytes)
            throw new InvalidOperationException($"Apple release receipt exceeds {MaximumReceiptBytes} bytes: {path}");

        PowerForgeAppleReleaseReceipt receipt;
        string payload;
        try
        {
            payload = File.ReadAllText(path);
            receipt = JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(
                          payload,
                          CreateOptions(writeIndented: false))
                      ?? throw new InvalidOperationException($"Apple release receipt is empty: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Apple release receipt is not valid JSON: {path}", exception);
        }

        if (receipt.SchemaVersion >= 4 && string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
        {
            throw new InvalidOperationException(
                $"Apple release receipt schema {receipt.SchemaVersion} is missing its required integrity SHA-256: {path}");
        }

        if (!string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
        {
            var expected = receipt.ReceiptSha256!.Trim();
            if (expected.Length != 64 || expected.Any(static value => !Uri.IsHexDigit(value)))
                throw new InvalidOperationException($"Apple release receipt has an invalid SHA-256: {path}");
            var actual = ComputeReceiptSha256Json(payload);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Apple release receipt integrity validation failed: {path}");
        }

        return receipt;
    }

    private static string ComputeReceiptSha256(JsonElement receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            WriteCanonicalJson(writer, receipt, omitReceiptHash: true);
        using var sha256 = SHA256.Create();
        return ToLowerHex(sha256.ComputeHash(stream.ToArray()));
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element, bool omitReceiptHash)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static value => value.Name, StringComparer.Ordinal))
                {
                    if (omitReceiptHash && property.Name.Equals("receiptSha256", StringComparison.OrdinalIgnoreCase))
                        continue;
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value, omitReceiptHash: false);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item, omitReceiptHash: false);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
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
            default:
                throw new InvalidOperationException($"Unsupported Apple receipt JSON value kind: {element.ValueKind}.");
        }
    }

    private static void PreserveLegacyLatest(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleReleaseReceipt? previousReceipt,
        string historyDirectory)
    {
        if (previousReceipt is null ||
            !string.IsNullOrWhiteSpace(previousReceipt.ReceiptSha256) ||
            !File.Exists(plan.ReceiptPath))
        {
            return;
        }

        var timestamp = previousReceipt.CheckedAt.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        var legacyPath = Path.Combine(historyDirectory, $"{timestamp}-legacy-{Guid.NewGuid():N}.json");
        WriteImmutableHistoryEntry(historyDirectory, legacyPath, File.ReadAllBytes(plan.ReceiptPath));
    }

    private static void WriteImmutableHistoryEntry(string historyDirectory, string destinationPath, string payload)
        => WriteImmutableHistoryEntry(
            historyDirectory,
            destinationPath,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payload));

    private static void WriteImmutableHistoryEntry(string historyDirectory, string destinationPath, byte[] payload)
    {
        var parent = Path.GetDirectoryName(historyDirectory) ?? historyDirectory;
        var temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(historyDirectory)}.{Guid.NewGuid():N}.receipt.tmp");
        try
        {
            WriteDurableBytes(temporaryPath, payload);
            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void WriteLatest(string projectRoot, string path, string payload, string settingName)
    {
        EnsureSafeOutputPath(projectRoot, path, settingName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            EnsureUnlinkedPath(projectRoot, directory, settingName);
        }

        var temporaryPath = Path.Combine(
            directory ?? projectRoot,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteDurableText(temporaryPath, payload);
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

    private static void WriteDurableText(string path, string payload)
        => WriteDurableBytes(
            path,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payload));

    private static void WriteDurableBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static string Serialize(PowerForgeAppleReleaseReceipt receipt)
        => JsonSerializer.Serialize(receipt, CreateOptions(writeIndented: true));

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
        => new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = writeIndented,
            Converters = { new JsonStringEnumConverter() }
        };

    private static string ToRelativePath(string projectRoot, string path)
        => FrameworkCompatibility.GetRelativePath(projectRoot, path).Replace('\\', '/');

    private static string ToLowerHex(byte[] value)
        => BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant();

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    private static void EnsureSafeOutputPath(string projectRoot, string path, string settingName)
    {
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsWithinRoot(fullPath, root))
            throw new InvalidOperationException($"{settingName} must remain inside AppleApps.ProjectRoot.");

        var current = fullPath;
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            current = Path.GetDirectoryName(current)
                      ?? throw new InvalidOperationException($"{settingName} could not be validated.");
        }
        EnsureUnlinkedPath(root, current, settingName);
    }

    private static void EnsureUnlinkedPath(string projectRoot, string path, string description)
    {
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsWithinRoot(current, root))
            throw new InvalidOperationException($"{description} must remain inside AppleApps.ProjectRoot: {current}");

        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"{description} must not traverse a symbolic link or reparse point: {current}");
            }

            if (current.Equals(root, PathComparison))
                return;
            current = Path.GetDirectoryName(current)
                      ?? throw new InvalidOperationException($"{description} could not be validated inside AppleApps.ProjectRoot.");
        }
    }

    private static bool IsWithinRoot(string path, string root)
        => path.Equals(root, PathComparison) ||
           path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
}
