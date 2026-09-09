using System.IO.Compression;
using System.Text;

namespace PowerForge;

internal static class PowerShellScriptArchiveValidator
{
    private const long MaxUncompressedArchiveBytes = 2L * 1024L * 1024L * 1024L;

    internal static bool TryValidate(
        string path,
        string? expectedEntryPoint,
        out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = "The archive file does not exist.";
            return false;
        }
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            error = "The artefact is not a ZIP archive.";
            return false;
        }

        string normalizedEntryPoint = (expectedEntryPoint ?? string.Empty)
            .Replace('\\', '/')
            .TrimStart('/')
            .Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(normalizedEntryPoint) ||
            !IsPortableArchiveEntryPath(normalizedEntryPoint) ||
            !string.Equals(Path.GetExtension(normalizedEntryPoint), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            error = "The recorded ScriptPacked entry point is missing or is not a portable .ps1 path.";
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 50000)
            {
                error = "The archive is empty or exceeds the 50,000-entry safety limit.";
                return false;
            }

            var archiveEntries = archive.Entries
                .Select(static entry => new
                {
                    Entry = entry,
                    RawPath = entry.FullName,
                    Path = entry.FullName.Replace('\\', '/'),
                    IsDirectory = string.IsNullOrWhiteSpace(entry.Name),
                    entry.ExternalAttributes
                })
                .ToArray();
            if (archiveEntries.Any(static entry => entry.RawPath.IndexOf('\\') >= 0))
            {
                error = "The archive contains a non-portable entry path with a backslash separator.";
                return false;
            }
            var entries = archiveEntries.Select(static entry => entry.Path).ToArray();
            var namespaceEntries = archiveEntries
                .Select(static entry => new
                {
                    Path = entry.Path.TrimEnd('/').Normalize(NormalizationForm.FormC),
                    entry.IsDirectory
                })
                .ToArray();
            var files = namespaceEntries
                .Where(static entry => !entry.IsDirectory)
                .Select(static entry => entry.Path)
                .ToArray();
            var fileEntries = archiveEntries
                .Where(static entry => !entry.IsDirectory)
                .Select(static entry => entry.Entry)
                .ToArray();

            if (files.Length == 0)
            {
                error = "The archive contains no files.";
                return false;
            }
            if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            {
                error = "The archive contains file paths that collide on a case-insensitive filesystem.";
                return false;
            }
            if (archiveEntries.Any(static entry => HasUnsupportedArchiveEntryType(
                    entry.ExternalAttributes,
                    entry.IsDirectory)))
            {
                error = "The archive contains a symbolic link, contradictory file type, or another unsupported entry type.";
                return false;
            }
            if (namespaceEntries
                .GroupBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1))
            {
                error = "The archive contains duplicate or file/directory-colliding paths.";
                return false;
            }
            if (files.Any(file => namespaceEntries.Any(entry =>
                    entry.Path.StartsWith(file + "/", StringComparison.OrdinalIgnoreCase))))
            {
                error = "The archive contains a file path that is also an ancestor of another entry.";
                return false;
            }
            if (entries.Any(static name => !IsPortableArchiveEntryPath(name)))
            {
                error = "The archive contains a rooted, traversing, or non-portable path.";
                return false;
            }
            if (files.Any(static name =>
                    name.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.github/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)))
            {
                error = "The archive contains repository or source-project content that is not a release payload.";
                return false;
            }
            if (files.Count(name => string.Equals(
                    name,
                    normalizedEntryPoint,
                    StringComparison.Ordinal)) != 1)
            {
                error = $"The archive does not contain exactly one recorded entry point '{normalizedEntryPoint}'.";
                return false;
            }

            ZipArchiveEntry entryPoint = archiveEntries.Single(entry =>
                !entry.IsDirectory &&
                string.Equals(
                    entry.Path.Normalize(NormalizationForm.FormC),
                    normalizedEntryPoint,
                    StringComparison.Ordinal)).Entry;
            if (ArchiveEntryStartsWithShebang(entryPoint) &&
                !HasUnixExecutePermission(entryPoint.ExternalAttributes))
            {
                error = $"The shebang entry point '{normalizedEntryPoint}' does not retain Unix execute permission.";
                return false;
            }

            if (!TryReadArchivePayloads(fileEntries, out error))
                return false;

            return true;
        }
        catch (Exception exception)
        {
            error = $"The archive could not be validated: {exception.Message}";
            return false;
        }
    }

    private static bool TryReadArchivePayloads(
        IReadOnlyList<ZipArchiveEntry> entries,
        out string? error)
    {
        error = null;
        long declaredBytes = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            if (entry.Length < 0 || entry.Length > MaxUncompressedArchiveBytes - declaredBytes)
            {
                error = "The archive exceeds the 2 GiB uncompressed payload safety limit.";
                return false;
            }
            declaredBytes += entry.Length;
        }

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            long entryBytesRead = 0;
            using Stream stream = entry.Open();
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (bytesRead > MaxUncompressedArchiveBytes - totalBytesRead)
                {
                    error = "The archive exceeds the 2 GiB uncompressed payload safety limit.";
                    return false;
                }
                totalBytesRead += bytesRead;
                entryBytesRead += bytesRead;
            }

            if (entryBytesRead != entry.Length)
            {
                error = $"The archive entry '{entry.FullName}' could not be read to its declared length.";
                return false;
            }
        }

        return true;
    }

    private static bool IsPortableArchivePathRooted(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path))
        {
            return true;
        }

        return path.Length >= 2 &&
               path[1] == ':' &&
               ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));
    }

    private static bool IsPortableArchiveEntryPath(string path)
    {
        if (IsPortableArchivePathRooted(path))
            return false;

        string[] segments = path.Split('/');
        int segmentCount = path.EndsWith("/", StringComparison.Ordinal)
            ? segments.Length - 1
            : segments.Length;
        if (segmentCount == 0)
            return false;

        for (int index = 0; index < segmentCount; index++)
        {
            string segment = segments[index];
            if (segment is "." or ".." || !ArtefactLayoutPathResolver.IsPortableFileName(segment))
                return false;
        }

        return segmentCount == segments.Length || string.IsNullOrEmpty(segments[segments.Length - 1]);
    }

    private static bool HasUnsupportedArchiveEntryType(int externalAttributes, bool isDirectory)
    {
        int unixFileType = (externalAttributes >> 16) & 0xF000;
        return (unixFileType is not 0 and not 0x4000 and not 0x8000) ||
               (unixFileType == 0x4000 && !isDirectory) ||
               (unixFileType == 0x8000 && isDirectory);
    }

    private static bool HasUnixExecutePermission(int externalAttributes)
        => (((externalAttributes >> 16) & 0x1FF) & 0x49) != 0;

    private static bool ArchiveEntryStartsWithShebang(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return stream.ReadByte() == '#' && stream.ReadByte() == '!';
    }
}
