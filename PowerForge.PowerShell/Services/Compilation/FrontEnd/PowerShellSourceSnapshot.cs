using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Shares authored source and parser offsets throughout one compilation operation.</summary>
internal sealed class PowerShellSourceSnapshot(string identityRoot, string? semanticProfileId)
{
    private readonly Dictionary<string, (ParsedSourceDocument Document, string Sha256)> _documents =
        new(PowerShellCompilationPathSafety.PathComparer);

    internal string SemanticProfileId { get; } = semanticProfileId ?? PowerShellSourceParser.HostSemanticProfileId;

    internal ParsedSourceDocument Get(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_documents)
        {
            if (_documents.TryGetValue(fullPath, out var entry))
                return entry.Document;

            var bytes = File.ReadAllBytes(fullPath);
            var document = PowerShellSourceParser.ParseFile(new MemoryStream(bytes, writable: false), fullPath, identityRoot, SemanticProfileId);
            using var sha256 = SHA256.Create();
            var hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            _documents.Add(fullPath, (document, hash));
            return document;
        }
    }

    internal string? GetCapturedSha256(string fullPath)
    {
        lock (_documents)
            return _documents.TryGetValue(fullPath, out var entry) ? entry.Sha256 : null;
    }
}
