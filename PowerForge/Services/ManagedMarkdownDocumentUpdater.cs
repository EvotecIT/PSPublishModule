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
        PrepareUpdates(new[] { request });
    }

    /// <summary>
    /// Creates or updates one managed block.
    /// </summary>
    /// <param name="request">Managed document update request.</param>
    /// <returns>Update result.</returns>
    public ManagedMarkdownUpdateResult Update(ManagedMarkdownUpdateRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return UpdateMany(new[] { request })[0];
    }

    /// <summary>
    /// Projects a batch of managed-block updates in memory, validates the final documents, and writes only after every update is valid.
    /// Documents targeted more than once are written once with their final projected content.
    /// </summary>
    /// <param name="requests">Managed document update requests.</param>
    /// <returns>One update result per request, in input order.</returns>
    public ManagedMarkdownUpdateResult[] UpdateMany(IEnumerable<ManagedMarkdownUpdateRequest> requests)
    {
        if (requests is null) throw new ArgumentNullException(nameof(requests));
        var batch = PrepareUpdates(requests);
        foreach (var document in batch.Documents)
        {
            if (!document.Changed)
                continue;

            var directory = System.IO.Path.GetDirectoryName(document.Path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            WriteDocument(document.Path, document.Document, document.Text);
        }
        return batch.Results;
    }

    private static PreparedBatch PrepareUpdates(IEnumerable<ManagedMarkdownUpdateRequest> requests)
    {
        var source = requests.ToArray();
        var documents = new Dictionary<string, PreparedDocument>(GetPathIdentityComparer());
        var orderedDocuments = new List<PreparedDocument>();
        var results = new ManagedMarkdownUpdateResult[source.Length];

        for (var index = 0; index < source.Length; index++)
        {
            var request = source[index] ?? throw new ArgumentException("Managed Markdown update requests cannot contain null entries.", nameof(requests));
            var normalized = NormalizeRequest(request);
            ValidateDestinationPath(normalized.Path);
            var identity = ResolvePathIdentity(normalized.Path);

            if (!documents.TryGetValue(identity.Key, out var document))
            {
                var ambiguousAlias = orderedDocuments.FirstOrDefault(existing =>
                    string.Equals(existing.Path, normalized.Path, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing.IdentityKey, identity.Key, StringComparison.Ordinal) &&
                    (!existing.IdentityWasResolved || !identity.WasResolved));
                if (ambiguousAlias is not null)
                {
                    throw new InvalidOperationException(
                        $"Managed Markdown paths '{ambiguousAlias.Path}' and '{normalized.Path}' may refer to the same destination. " +
                        "Use one canonical path casing for a batch that creates a new document.");
                }

                var exists = File.Exists(normalized.Path);
                document = new PreparedDocument(
                    normalized.Path,
                    identity.Key,
                    identity.WasResolved,
                    exists ? ReadDocument(normalized.Path) : DocumentText.NewUtf8(),
                    exists);
                documents.Add(identity.Key, document);
                orderedDocuments.Add(document);
            }

            normalized.OriginalLocation = document.Exists
                ? FindBlock(
                    document.Document.Text,
                    normalized.MarkerNamespace,
                    normalized.BlockId,
                    normalized.MarkerFormat)
                : null;
            foreach (var prior in document.Requests)
            {
                if (TargetsCanMatchSameMarker(prior, normalized))
                {
                    throw new InvalidOperationException(
                        $"Managed block '{normalized.BlockId}' is targeted more than once in '{document.Path}'.");
                }
                if (prior.OriginalLocation is not null && normalized.OriginalLocation is not null &&
                    BlocksOverlap(prior.OriginalLocation, normalized.OriginalLocation))
                {
                    throw new InvalidOperationException(
                        $"Managed blocks '{prior.BlockId}' and '{normalized.BlockId}' overlap in '{document.Path}'.");
                }
            }

            var before = document.Text;
            string updated;
            var created = false;
            var appended = false;

            if (!document.Exists)
            {
                if (!normalized.CreateIfMissing)
                    throw new FileNotFoundException($"Managed Markdown document was not found: {normalized.Path}", normalized.Path);

                updated = CreateDocument(
                    normalized.MarkerNamespace,
                    normalized.BlockId,
                    normalized.Markdown,
                    normalized.NewDocumentTitle,
                    Environment.NewLine,
                    normalized.MarkerFormat);
                created = true;
            }
            else
            {
                var location = FindBlock(
                    document.Text,
                    normalized.MarkerNamespace,
                    normalized.BlockId,
                    normalized.MarkerFormat);
                if (location is null)
                {
                    if (normalized.MissingBlockBehavior != ManagedMarkdownMissingBlockBehavior.Append)
                        throw new InvalidOperationException($"Managed block '{normalized.BlockId}' was not found in '{normalized.Path}'.");

                    updated = AppendBlock(
                        document.Text,
                        normalized.MarkerNamespace,
                        normalized.BlockId,
                        normalized.Markdown,
                        DetectPreferredLineEnding(document.Text),
                        normalized.MarkerFormat);
                    appended = true;
                }
                else
                {
                    updated = ReplaceBlock(document.Text, location, normalized.Markdown);
                }
            }

            document.Text = updated;
            document.Exists = true;
            document.Requests.Add(normalized);
            results[index] = new ManagedMarkdownUpdateResult
            {
                Path = normalized.Path,
                BlockId = normalized.BlockId,
                Changed = !string.Equals(before, updated, StringComparison.Ordinal),
                Created = created,
                Appended = appended
            };
            document.ResultIndexes.Add(index);
        }

        foreach (var document in orderedDocuments)
        {
            var finalLocations = new List<(NormalizedUpdateRequest Request, BlockLocation Location)>();
            foreach (var request in document.Requests)
            {
                var location = FindBlock(document.Text, request.MarkerNamespace, request.BlockId, request.MarkerFormat);
                if (location is null)
                {
                    throw new InvalidOperationException(
                        $"Managed Markdown batch update did not preserve block '{request.BlockId}' in '{document.Path}'. " +
                        "Managed blocks in the same document cannot overlap.");
                }
                foreach (var prior in finalLocations)
                {
                    if (BlocksOverlap(prior.Location, location))
                    {
                        throw new InvalidOperationException(
                            $"Managed Markdown batch update produced overlapping blocks '{prior.Request.BlockId}' and " +
                            $"'{request.BlockId}' in '{document.Path}'.");
                    }
                }
                finalLocations.Add((request, location));
            }
            document.Changed = !string.Equals(document.Document.Text, document.Text, StringComparison.Ordinal);
            foreach (var resultIndex in document.ResultIndexes)
                results[resultIndex].Changed = document.Changed;
        }

        return new PreparedBatch(orderedDocuments.ToArray(), results);
    }

    private static NormalizedUpdateRequest NormalizeRequest(ManagedMarkdownUpdateRequest request)
        => new(
            NormalizePath(request.Path),
            NormalizeMarkerNamespace(request.MarkerNamespace),
            NormalizeBlockId(request.BlockId),
            NormalizeMarkerFormat(request.MarkerFormat),
            NormalizeMarkdown(request.Markdown),
            request.CreateIfMissing,
            request.MissingBlockBehavior,
            request.NewDocumentTitle);

    private static bool TargetsCanMatchSameMarker(NormalizedUpdateRequest first, NormalizedUpdateRequest second)
    {
        if (!string.Equals(first.BlockId, second.BlockId, StringComparison.OrdinalIgnoreCase))
            return false;

        var shareLegacy = first.MarkerFormat != ManagedMarkdownMarkerFormat.Namespaced &&
                          second.MarkerFormat != ManagedMarkdownMarkerFormat.Namespaced;
        var shareNamespaced = first.MarkerFormat != ManagedMarkdownMarkerFormat.LegacyBlockId &&
                              second.MarkerFormat != ManagedMarkdownMarkerFormat.LegacyBlockId &&
                              string.Equals(first.MarkerNamespace, second.MarkerNamespace, StringComparison.OrdinalIgnoreCase);
        return shareLegacy || shareNamespaced;
    }

    private static bool BlocksOverlap(BlockLocation first, BlockLocation second)
        => first.StartLine.Start < second.EndLine.End && second.StartLine.Start < first.EndLine.End;

    private static StringComparer GetPathIdentityComparer()
        => System.IO.Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static PathIdentity ResolvePathIdentity(string path)
    {
        if (!File.Exists(path))
            return new PathIdentity(path, wasResolved: false);

        try
        {
            return new PathIdentity(ExistingFilePathIdentityResolver.Resolve(path), wasResolved: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                $"Managed Markdown document identity could not be resolved safely: {path}",
                exception);
        }
    }

    /// <summary>
    /// Validates that a managed block exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Managed block identifier.</param>
    public void ValidateBlock(string path, string blockId)
        => ValidateBlock(path, blockId, "POWERFORGE");

    /// <summary>
    /// Validates that a managed block with the specified marker namespace exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Managed block identifier.</param>
    /// <param name="markerNamespace">Expected marker namespace.</param>
    public void ValidateBlock(string path, string blockId, string markerNamespace)
        => ValidateBlock(path, blockId, markerNamespace, ManagedMarkdownMarkerFormat.Namespaced);

    /// <summary>
    /// Validates that a managed block with the specified namespace and marker format exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Managed block identifier.</param>
    /// <param name="markerNamespace">Expected marker namespace.</param>
    /// <param name="markerFormat">Accepted marker syntax.</param>
    public void ValidateBlock(
        string path,
        string blockId,
        string markerNamespace,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        var fullPath = NormalizePath(path);
        var normalizedMarkerNamespace = NormalizeMarkerNamespace(markerNamespace);
        var normalizedBlockId = NormalizeBlockId(blockId);
        var normalizedMarkerFormat = NormalizeMarkerFormat(markerFormat);
        ValidateDestinationPath(fullPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Managed Markdown document was not found: {fullPath}", fullPath);

        var content = ReadDocument(fullPath).Text;
        if (FindBlock(content, normalizedMarkerNamespace, normalizedBlockId, normalizedMarkerFormat) is null)
            throw new InvalidOperationException($"Managed block '{normalizedBlockId}' was not found in '{fullPath}'.");
    }

    private static string CreateDocument(
        string markerNamespace,
        string blockId,
        string markdown,
        string? title,
        string newline,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("# ").Append(title!.Trim()).Append(newline).Append(newline);
        }

        AppendManagedBlock(builder, markerNamespace, blockId, markdown, newline, markerFormat);
        return builder.ToString();
    }

    private static string AppendBlock(
        string original,
        string markerNamespace,
        string blockId,
        string markdown,
        string newline,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        var builder = new StringBuilder(original);
        if (builder.Length > 0)
        {
            if (!EndsWithLineEnding(original))
                builder.Append(newline);
            builder.Append(newline);
        }
        AppendManagedBlock(builder, markerNamespace, blockId, markdown, newline, markerFormat);
        return builder.ToString();
    }

    private static void AppendManagedBlock(
        StringBuilder builder,
        string markerNamespace,
        string blockId,
        string markdown,
        string newline,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        var writeFormat = markerFormat == ManagedMarkdownMarkerFormat.LegacyBlockId
            ? ManagedMarkdownMarkerFormat.LegacyBlockId
            : ManagedMarkdownMarkerFormat.Namespaced;
        AppendMarker(builder, markerNamespace, blockId, "START", newline, writeFormat);
        if (markdown.Length > 0)
            builder.Append(ConvertLineEndings(markdown, newline)).Append(newline);
        AppendMarker(builder, markerNamespace, blockId, "END", newline, writeFormat);
    }

    private static void AppendMarker(
        StringBuilder builder,
        string markerNamespace,
        string blockId,
        string side,
        string newline,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        builder.Append("<!-- ");
        if (markerFormat == ManagedMarkdownMarkerFormat.Namespaced)
            builder.Append(markerNamespace).Append(':');
        builder.Append(blockId).Append(':').Append(side).Append(" -->").Append(newline);
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

    private static BlockLocation? FindBlock(
        string content,
        string markerNamespace,
        string blockId,
        ManagedMarkdownMarkerFormat markerFormat)
    {
        if (markerFormat != ManagedMarkdownMarkerFormat.NamespacedOrLegacyBlockId)
            return FindBlock(content, markerNamespace, blockId, markerFormat == ManagedMarkdownMarkerFormat.LegacyBlockId);

        var namespaced = FindBlock(content, markerNamespace, blockId, legacy: false);
        var legacy = FindBlock(content, markerNamespace, blockId, legacy: true);
        if (namespaced is not null && legacy is not null)
            throw new InvalidOperationException($"Managed block '{blockId}' has both namespaced and legacy markers.");
        return namespaced ?? legacy;
    }

    private static BlockLocation? FindBlock(string content, string markerNamespace, string blockId, bool legacy)
    {
        var starts = new List<LineLocation>();
        var ends = new List<LineLocation>();
        foreach (var line in EnumerateLines(content))
        {
            var value = content.Substring(line.Start, line.ContentEnd - line.Start);
            if (IsMarker(value, markerNamespace, blockId, "START", legacy)) starts.Add(line);
            if (IsMarker(value, markerNamespace, blockId, "END", legacy)) ends.Add(line);
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

    private static bool IsMarker(string? line, string markerNamespace, string blockId, string side, bool legacy)
    {
        var text = (line ?? string.Empty).Trim();
        if (!text.StartsWith("<!--", StringComparison.Ordinal) || !text.EndsWith("-->", StringComparison.Ordinal))
            return false;

        var body = text.Substring(4, text.Length - 7).Trim();
        var parts = body.Split(':').Select(part => part.Trim()).ToArray();
        if (legacy)
        {
            return parts.Length == 2
                   && string.Equals(parts[0], blockId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(parts[1], side, StringComparison.OrdinalIgnoreCase);
        }

        return parts.Length == 3
               && string.Equals(parts[0], markerNamespace, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[1], blockId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[2], side, StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeMarkerNamespace(string? markerNamespace)
    {
        if (string.IsNullOrWhiteSpace(markerNamespace)) throw new ArgumentException("Marker namespace is required.", nameof(markerNamespace));
        var normalized = markerNamespace!.Trim();
        if (normalized.Contains(':') || normalized.Contains('\r') || normalized.Contains('\n') || normalized.Contains("-->", StringComparison.Ordinal))
            throw new ArgumentException("Marker namespace cannot contain colons, line breaks, or an HTML comment terminator.", nameof(markerNamespace));
        return normalized;
    }

    private static ManagedMarkdownMarkerFormat NormalizeMarkerFormat(ManagedMarkdownMarkerFormat markerFormat)
    {
        if (!Enum.IsDefined(typeof(ManagedMarkdownMarkerFormat), markerFormat))
            throw new ArgumentOutOfRangeException(nameof(markerFormat), markerFormat, "Unsupported managed Markdown marker format.");
        return markerFormat;
    }

    private static void ValidateDestinationPath(string path)
    {
        if (Directory.Exists(path))
            throw new IOException($"Managed Markdown document path is an existing directory: {path}");

        var parent = System.IO.Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(parent))
        {
            if (File.Exists(parent))
            {
                throw new IOException(
                    $"Managed Markdown document path has an existing file ancestor '{parent}': {path}");
            }
            if (Directory.Exists(parent))
                return;

            var next = System.IO.Path.GetDirectoryName(parent);
            if (string.Equals(next, parent, StringComparison.Ordinal))
                return;
            parent = next;
        }
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

    private sealed class NormalizedUpdateRequest
    {
        internal NormalizedUpdateRequest(
            string path,
            string markerNamespace,
            string blockId,
            ManagedMarkdownMarkerFormat markerFormat,
            string markdown,
            bool createIfMissing,
            ManagedMarkdownMissingBlockBehavior missingBlockBehavior,
            string? newDocumentTitle)
        {
            Path = path;
            MarkerNamespace = markerNamespace;
            BlockId = blockId;
            MarkerFormat = markerFormat;
            Markdown = markdown;
            CreateIfMissing = createIfMissing;
            MissingBlockBehavior = missingBlockBehavior;
            NewDocumentTitle = newDocumentTitle;
        }

        internal string Path { get; }
        internal string MarkerNamespace { get; }
        internal string BlockId { get; }
        internal ManagedMarkdownMarkerFormat MarkerFormat { get; }
        internal string Markdown { get; }
        internal bool CreateIfMissing { get; }
        internal ManagedMarkdownMissingBlockBehavior MissingBlockBehavior { get; }
        internal string? NewDocumentTitle { get; }
        internal BlockLocation? OriginalLocation { get; set; }
    }

    private sealed class PreparedDocument
    {
        internal PreparedDocument(string path, string identityKey, bool identityWasResolved, DocumentText document, bool exists)
        {
            Path = path;
            IdentityKey = identityKey;
            IdentityWasResolved = identityWasResolved;
            Document = document;
            Text = document.Text;
            Exists = exists;
        }

        internal string Path { get; }
        internal string IdentityKey { get; }
        internal bool IdentityWasResolved { get; }
        internal DocumentText Document { get; }
        internal List<NormalizedUpdateRequest> Requests { get; } = new();
        internal List<int> ResultIndexes { get; } = new();
        internal string Text { get; set; }
        internal bool Exists { get; set; }
        internal bool Changed { get; set; }
    }

    private readonly struct PathIdentity
    {
        internal PathIdentity(string key, bool wasResolved)
        {
            Key = key;
            WasResolved = wasResolved;
        }

        internal string Key { get; }
        internal bool WasResolved { get; }
    }

    private sealed class PreparedBatch
    {
        internal PreparedBatch(PreparedDocument[] documents, ManagedMarkdownUpdateResult[] results)
        {
            Documents = documents;
            Results = results;
        }

        internal PreparedDocument[] Documents { get; }
        internal ManagedMarkdownUpdateResult[] Results { get; }
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
