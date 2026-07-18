using System.Text;

namespace PowerForge;

/// <summary>
/// Creates and updates marker-delimited blocks in Markdown documents.
/// </summary>
public sealed class ManagedMarkdownDocumentUpdater
{
    /// <summary>
    /// Validates whether an update can be applied without writing the document.
    /// </summary>
    /// <param name="request">Managed document update request.</param>
    public void ValidateUpdate(ManagedMarkdownUpdateRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var path = NormalizePath(request.Path);
        var blockId = NormalizeBlockId(request.BlockId);
        if (!File.Exists(path))
        {
            if (!request.CreateIfMissing)
                throw new FileNotFoundException($"Managed Markdown document was not found: {path}", path);
            return;
        }

        var document = ReadDocument(path);
        var location = FindBlock(document.Text, blockId);
        if (location is null && request.MissingBlockBehavior != ManagedMarkdownMissingBlockBehavior.Append)
            throw new InvalidOperationException($"Managed block '{blockId}' was not found in '{path}'.");
    }

    /// <summary>
    /// Creates or updates one managed block.
    /// </summary>
    /// <param name="request">Managed document update request.</param>
    /// <returns>Update result.</returns>
    public ManagedMarkdownUpdateResult Update(ManagedMarkdownUpdateRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var path = NormalizePath(request.Path);
        var blockId = NormalizeBlockId(request.BlockId);
        var replacement = NormalizeMarkdown(request.Markdown);
        var exists = File.Exists(path);

        DocumentText document;
        string updated;
        var created = false;
        var appended = false;

        if (!exists)
        {
            if (!request.CreateIfMissing)
                throw new FileNotFoundException($"Managed Markdown document was not found: {path}", path);

            document = DocumentText.NewUtf8();
            updated = CreateDocument(blockId, replacement, request.NewDocumentTitle, Environment.NewLine);
            created = true;
        }
        else
        {
            document = ReadDocument(path);
            var location = FindBlock(document.Text, blockId);

            if (location is null)
            {
                if (request.MissingBlockBehavior != ManagedMarkdownMissingBlockBehavior.Append)
                    throw new InvalidOperationException($"Managed block '{blockId}' was not found in '{path}'.");

                updated = AppendBlock(document.Text, blockId, replacement, DetectPreferredLineEnding(document.Text));
                appended = true;
            }
            else
            {
                updated = ReplaceBlock(document.Text, location, replacement);
            }
        }

        var changed = !string.Equals(document.Text, updated, StringComparison.Ordinal);
        if (changed)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            WriteDocument(path, document, updated);
        }

        return new ManagedMarkdownUpdateResult
        {
            Path = path,
            BlockId = blockId,
            Changed = changed,
            Created = created,
            Appended = appended
        };
    }

    /// <summary>
    /// Validates that a managed block exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Managed block identifier.</param>
    public void ValidateBlock(string path, string blockId)
    {
        var fullPath = NormalizePath(path);
        var normalizedBlockId = NormalizeBlockId(blockId);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Managed Markdown document was not found: {fullPath}", fullPath);

        var content = ReadDocument(fullPath).Text;
        if (FindBlock(content, normalizedBlockId) is null)
            throw new InvalidOperationException($"Managed block '{normalizedBlockId}' was not found in '{fullPath}'.");
    }

    private static string CreateDocument(string blockId, string markdown, string? title, string newline)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("# ").Append(title!.Trim()).Append(newline).Append(newline);
        }

        AppendManagedBlock(builder, blockId, markdown, newline);
        return builder.ToString();
    }

    private static string AppendBlock(string original, string blockId, string markdown, string newline)
    {
        var builder = new StringBuilder(original);
        if (builder.Length > 0)
        {
            if (!EndsWithLineEnding(original))
                builder.Append(newline);
            builder.Append(newline);
        }
        AppendManagedBlock(builder, blockId, markdown, newline);
        return builder.ToString();
    }

    private static void AppendManagedBlock(StringBuilder builder, string blockId, string markdown, string newline)
    {
        builder.Append("<!-- POWERFORGE:").Append(blockId).Append(":START -->").Append(newline);
        if (markdown.Length > 0)
            builder.Append(ConvertLineEndings(markdown, newline)).Append(newline);
        builder.Append("<!-- POWERFORGE:").Append(blockId).Append(":END -->").Append(newline);
    }

    private static string ReplaceBlock(string original, BlockLocation block, string replacement)
    {
        var newline = block.StartLine.LineEnding.Length > 0
            ? block.StartLine.LineEnding
            : DetectPreferredLineEnding(original);
        var prefix = original.Substring(0, block.StartLine.End);
        var suffix = original.Substring(block.EndLine.Start);
        if (replacement.Length == 0)
            return prefix + suffix;
        return prefix + ConvertLineEndings(replacement, newline) + newline + suffix;
    }

    private static BlockLocation? FindBlock(string content, string blockId)
    {
        var starts = new List<LineLocation>();
        var ends = new List<LineLocation>();
        foreach (var line in EnumerateLines(content))
        {
            var value = content.Substring(line.Start, line.ContentEnd - line.Start);
            if (IsMarker(value, blockId, "START")) starts.Add(line);
            if (IsMarker(value, blockId, "END")) ends.Add(line);
        }

        if (starts.Count == 0 && ends.Count == 0)
            return null;
        if (starts.Count != 1 || ends.Count != 1 || ends[0].Start <= starts[0].Start)
            throw new InvalidOperationException($"Managed block '{blockId}' has missing, duplicate, or out-of-order markers.");

        return new BlockLocation(starts[0], ends[0]);
    }

    private static IEnumerable<LineLocation> EnumerateLines(string content)
    {
        var index = 0;
        while (index < content.Length)
        {
            var start = index;
            while (index < content.Length && content[index] != '\r' && content[index] != '\n')
                index++;
            var contentEnd = index;
            var lineEndingStart = index;
            if (index < content.Length && content[index] == '\r')
            {
                index++;
                if (index < content.Length && content[index] == '\n') index++;
            }
            else if (index < content.Length)
            {
                index++;
            }
            yield return new LineLocation(start, contentEnd, index, content.Substring(lineEndingStart, index - lineEndingStart));
        }
    }

    private static bool IsMarker(string? line, string blockId, string side)
    {
        var text = (line ?? string.Empty).Trim();
        if (!text.StartsWith("<!--", StringComparison.Ordinal) || !text.EndsWith("-->", StringComparison.Ordinal))
            return false;

        var body = text.Substring(4, text.Length - 7).Trim();
        var parts = body.Split(':').Select(part => part.Trim()).ToArray();
        return parts.Length >= 2
               && string.Equals(parts[parts.Length - 2], blockId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[parts.Length - 1], side, StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentText ReadDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes, out var preambleLength);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var preamble = new byte[preambleLength];
        if (preambleLength > 0)
            Buffer.BlockCopy(bytes, 0, preamble, 0, preambleLength);
        return new DocumentText(text, encoding, preamble);
    }

    private static void WriteDocument(string path, DocumentText document, string text)
    {
        var content = document.Encoding.GetBytes(text);
        if (document.Preamble.Length == 0)
        {
            File.WriteAllBytes(path, content);
            return;
        }

        var bytes = new byte[document.Preamble.Length + content.Length];
        Buffer.BlockCopy(document.Preamble, 0, bytes, 0, document.Preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, document.Preamble.Length, content.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
    {
        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
        }
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
        }
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
        }
        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
        }

        preambleLength = 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index]) return false;
        }
        return true;
    }

    private static string DetectPreferredLineEnding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\r')
                return index + 1 < value.Length && value[index + 1] == '\n' ? "\r\n" : "\r";
            if (value[index] == '\n')
                return "\n";
        }
        return Environment.NewLine;
    }

    private static bool EndsWithLineEnding(string value)
        => value.EndsWith("\n", StringComparison.Ordinal) || value.EndsWith("\r", StringComparison.Ordinal);

    private static string ConvertLineEndings(string value, string newline)
        => NormalizeLineEndings(value).Replace("\n", newline);

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Document path is required.", nameof(path));
        return System.IO.Path.GetFullPath(path!.Trim().Trim('"'));
    }

    private static string NormalizeBlockId(string? blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("Block id is required.", nameof(blockId));
        var normalized = blockId!.Trim();
        if (normalized.Contains(':') || normalized.Contains('\r') || normalized.Contains('\n') || normalized.Contains("-->", StringComparison.Ordinal))
            throw new ArgumentException("Block id cannot contain colons, line breaks, or an HTML comment terminator.", nameof(blockId));
        return normalized;
    }

    private static string NormalizeMarkdown(string? markdown)
        => NormalizeLineEndings(markdown ?? string.Empty).TrimEnd('\n');

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private sealed class DocumentText
    {
        internal DocumentText(string text, Encoding encoding, byte[] preamble)
        {
            Text = text;
            Encoding = encoding;
            Preamble = preamble;
        }

        internal string Text { get; }
        internal Encoding Encoding { get; }
        internal byte[] Preamble { get; }

        internal static DocumentText NewUtf8()
            => new(string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Array.Empty<byte>());
    }

    private sealed class LineLocation
    {
        internal LineLocation(int start, int contentEnd, int end, string lineEnding)
        {
            Start = start;
            ContentEnd = contentEnd;
            End = end;
            LineEnding = lineEnding;
        }

        internal int Start { get; }
        internal int ContentEnd { get; }
        internal int End { get; }
        internal string LineEnding { get; }
    }

    private sealed class BlockLocation
    {
        internal BlockLocation(LineLocation startLine, LineLocation endLine)
        {
            StartLine = startLine;
            EndLine = endLine;
        }

        internal LineLocation StartLine { get; }
        internal LineLocation EndLine { get; }
    }
}
