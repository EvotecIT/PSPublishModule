using System.Text;

namespace PowerForge;

/// <summary>
/// Abstraction over basic file system operations to facilitate testing and reuse.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Returns <c>true</c> if the file exists at the specified <paramref name="path"/>.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Reads the entire file contents as text using the platform default encoding.
    /// </summary>
    string ReadAllText(string path);

    /// <summary>
    /// Writes the specified <paramref name="contents"/> to <paramref name="path"/> using <paramref name="encoding"/>.
    /// </summary>
    void WriteAllText(string path, string contents, Encoding encoding);

    /// <summary>
    /// Enumerates files under <paramref name="root"/> matching <paramref name="searchPattern"/>.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string root, string searchPattern, SearchOption option);
}

/// <summary>
/// Real file system implementation delegating to <see cref="System.IO"/> APIs.
/// </summary>
public sealed class RealFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);
    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);
    /// <inheritdoc />
    public void WriteAllText(string path, string contents, Encoding encoding) => File.WriteAllText(path, contents, encoding);
    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string root, string searchPattern, SearchOption option) => Directory.EnumerateFiles(root, searchPattern, option);
}
