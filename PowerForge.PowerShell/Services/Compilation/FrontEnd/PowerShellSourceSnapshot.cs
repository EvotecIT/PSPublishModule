using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Shares authored source and parser offsets throughout one compilation operation.</summary>
internal sealed class PowerShellSourceSnapshot(string identityRoot, string? semanticProfileId)
{
    private sealed record SourceEntry(ParsedSourceDocument Document, string Sha256, bool HasUnmarkedNonAsciiBytes);

    private static readonly byte[][] UnicodePreambles =
    {
        Encoding.UTF8.GetPreamble(), Encoding.Unicode.GetPreamble(), Encoding.BigEndianUnicode.GetPreamble(),
        Encoding.UTF32.GetPreamble(), new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetPreamble()
    };

    private readonly Dictionary<string, SourceEntry> _documents =
        new(PowerShellCompilationPathSafety.PathComparer);

    internal string SemanticProfileId { get; } = semanticProfileId ?? PowerShellSourceParser.HostSemanticProfileId;

    internal ParsedSourceDocument Get(string path) => Capture(path, path).Document;

    private SourceEntry Capture(string path, string readPath)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_documents)
        {
            if (_documents.TryGetValue(fullPath, out var entry))
                return entry;

            var bytes = File.ReadAllBytes(readPath);
            var document = PowerShellSourceParser.ParseFile(new MemoryStream(bytes, writable: false), fullPath, identityRoot, SemanticProfileId);
            using var sha256 = SHA256.Create();
            var hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            var unmarkedNonAscii = Array.Exists(bytes, static value => value >= 0x80) &&
                !UnicodePreambles.Any(preamble => bytes.AsSpan().StartsWith(preamble.AsSpan()));
            entry = new SourceEntry(document, hash, unmarkedNonAscii);
            _documents.Add(fullPath, entry);
            return entry;
        }
    }

    internal string? GetDeliveryRewrite(string sourcePath, string deliveryProfile, string? stagedPath = null)
    {
        if (deliveryProfile == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
        {
            // A recipient may use a different ANSI code page, even if the build host
            // decodes these bytes identically under both profiles.
            var entry = Capture(sourcePath, stagedPath ?? sourcePath);
            return entry.HasUnmarkedNonAsciiBytes ? entry.Document.Text : null;
        }
        if (deliveryProfile == SemanticProfileId) return null;
        var document = Capture(sourcePath, stagedPath ?? sourcePath).Document;
        try
        {
            if (PowerShellSourceParser.ReadText(File.OpenRead(stagedPath ?? sourcePath), deliveryProfile)
                .Equals(document.Text, StringComparison.Ordinal))
                return null;
        }
        catch (System.Text.DecoderFallbackException)
        {
            // The delivery host requires a BOM to decode bytes outside ASCII.
        }
        return document.Text;
    }

    internal string? GetCapturedSha256(string fullPath)
    {
        lock (_documents)
            return _documents.TryGetValue(fullPath, out var entry) ? entry.Sha256 : null;
    }
}
