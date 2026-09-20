namespace PowerForge;

/// <summary>An immutable text snapshot used to detect changes before saving a draft.</summary>
public sealed class RepositoryTextDocument
{
    internal RepositoryTextDocument(string path, string text, string hash, string encodingName, int maxBytes)
    {
        Path = path;
        Text = text;
        ContentHash = hash;
        EncodingName = encodingName;
        MaxBytes = maxBytes;
    }

    /// <summary>Absolute file path.</summary>
    public string Path { get; }
    /// <summary>Original decoded text, including its line endings.</summary>
    public string Text { get; }
    /// <summary>SHA-256 of the original bytes, including any byte-order mark.</summary>
    public string ContentHash { get; }
    /// <summary>Name of the detected Unicode encoding.</summary>
    public string EncodingName { get; }
    internal int MaxBytes { get; }

    /// <summary>Rebinds the original byte snapshot after a caller has moved the file. Saving still verifies those bytes at the new path.</summary>
    public RepositoryTextDocument WithPath(string path)
        => new(System.IO.Path.GetFullPath(path), Text, ContentHash, EncodingName, MaxBytes);
}
