using System.Text;

namespace PowerForge;

internal sealed class RepositoryTextFileUpdate
{
    public RepositoryTextFileUpdate(string filePath, string originalContent, string updatedContent)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        OriginalContent = originalContent ?? throw new ArgumentNullException(nameof(originalContent));
        UpdatedContent = updatedContent ?? throw new ArgumentNullException(nameof(updatedContent));
    }

    public string FilePath { get; }
    public string OriginalContent { get; }
    public string UpdatedContent { get; }
}

internal sealed class RepositoryTextFileTransactionService
{
    internal delegate void ReplaceFileHandler(string sourcePath, string destinationPath, string backupPath);

    private readonly ReplaceFileHandler _replaceFile;

    public RepositoryTextFileTransactionService()
        : this(static (sourcePath, destinationPath, backupPath) =>
            File.Replace(sourcePath, destinationPath, backupPath))
    {
    }

    internal RepositoryTextFileTransactionService(ReplaceFileHandler replaceFile)
    {
        _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
    }

    public void Apply(IReadOnlyList<RepositoryTextFileUpdate> updates)
    {
        if (updates is null)
            throw new ArgumentNullException(nameof(updates));
        if (updates.Count == 0)
            return;

        var comparison = FrameworkCompatibility.GetPathStringComparison(
            Path.GetDirectoryName(updates[0].FilePath) ?? updates[0].FilePath);
        var comparer = comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var prepared = new List<PreparedUpdate>(updates.Count);
        var uniquePaths = new HashSet<string>(comparer);
        var preserveBackups = false;

        try
        {
            foreach (var update in updates)
            {
                var fullPath = Path.GetFullPath(update.FilePath);
                if (!uniquePaths.Add(fullPath))
                    throw new InvalidOperationException($"A release file update was configured more than once: {fullPath}");
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"Release file update target was not found: {fullPath}", fullPath);

                var snapshot = ReadSnapshot(fullPath);
                var currentContent = snapshot.Content;
                if (!string.Equals(currentContent, update.OriginalContent, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Release file changed after version planning and was not modified: {fullPath}");
                if (string.Equals(currentContent, update.UpdatedContent, StringComparison.Ordinal))
                    continue;

                var suffix = ".powerforge-" + Guid.NewGuid().ToString("N");
                var temporaryPath = fullPath + suffix + ".tmp";
                var backupPath = fullPath + suffix + ".bak";
                WriteSnapshot(temporaryPath, update.UpdatedContent, snapshot);
#if !NET472
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(fullPath));
#endif
                prepared.Add(new PreparedUpdate(fullPath, temporaryPath, backupPath));
            }

            var replaced = new List<PreparedUpdate>(prepared.Count);
            try
            {
                foreach (var update in prepared)
                {
                    _replaceFile(update.TemporaryPath, update.FilePath, update.BackupPath);
                    replaced.Add(update);
                }
            }
            catch (Exception writeException)
            {
                var rollbackErrors = RollBack(replaced);
                preserveBackups = rollbackErrors.Count > 0;
                var message = rollbackErrors.Count == 0
                    ? "Release file transaction failed; prior file replacements were rolled back."
                    : "Release file transaction failed and one or more prior file replacements could not be rolled back: "
                      + string.Join(" | ", rollbackErrors);
                throw new InvalidOperationException(message, writeException);
            }
        }
        finally
        {
            foreach (var update in prepared)
            {
                TryDelete(update.TemporaryPath);
                if (!preserveBackups)
                    TryDelete(update.BackupPath);
            }
        }
    }

    private static List<string> RollBack(IReadOnlyList<PreparedUpdate> replaced)
    {
        var errors = new List<string>();
        for (var index = replaced.Count - 1; index >= 0; index--)
        {
            var update = replaced[index];
            try
            {
                if (!File.Exists(update.BackupPath))
                    throw new FileNotFoundException("Release file transaction backup was not found.", update.BackupPath);

                if (File.Exists(update.FilePath))
                    File.Replace(update.BackupPath, update.FilePath, destinationBackupFileName: null);
                else
                    File.Move(update.BackupPath, update.FilePath);
            }
            catch (Exception ex)
            {
                errors.Add($"{update.FilePath}: {ex.Message}");
            }
        }

        return errors;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup. A rollback error is reported separately.
        }
    }

    private static TextFileSnapshot ReadSnapshot(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var preambleLength = 0;

        if (StartsWith(bytes, new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            preambleLength = 4;
        }
        else if (StartsWith(bytes, new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
            preambleLength = 4;
        }
        else if (StartsWith(bytes, new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            preambleLength = 3;
        }
        else if (StartsWith(bytes, new byte[] { 0xFE, 0xFF }))
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            preambleLength = 2;
        }
        else if (StartsWith(bytes, new byte[] { 0xFF, 0xFE }))
        {
            encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            preambleLength = 2;
        }

        return new TextFileSnapshot(
            encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength),
            encoding,
            preambleLength == 0 ? Array.Empty<byte>() : encoding.GetPreamble());
    }

    private static void WriteSnapshot(string path, string content, TextFileSnapshot snapshot)
    {
        var contentBytes = snapshot.Encoding.GetBytes(content);
        var bytes = new byte[snapshot.Preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(snapshot.Preamble, 0, bytes, 0, snapshot.Preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, bytes, snapshot.Preamble.Length, contentBytes.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
                return false;
        }

        return true;
    }

    private sealed class PreparedUpdate
    {
        public PreparedUpdate(string filePath, string temporaryPath, string backupPath)
        {
            FilePath = filePath;
            TemporaryPath = temporaryPath;
            BackupPath = backupPath;
        }

        public string FilePath { get; }
        public string TemporaryPath { get; }
        public string BackupPath { get; }
    }

    private sealed class TextFileSnapshot
    {
        public TextFileSnapshot(string content, Encoding encoding, byte[] preamble)
        {
            Content = content;
            Encoding = encoding;
            Preamble = preamble;
        }

        public string Content { get; }
        public Encoding Encoding { get; }
        public byte[] Preamble { get; }
    }
}
