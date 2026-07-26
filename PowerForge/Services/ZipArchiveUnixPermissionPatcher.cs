using System.IO.Compression;

namespace PowerForge;

/// <summary>
/// Applies Unix executable metadata directly to ZIP central-directory entries.
/// </summary>
/// <remarks>
/// <see cref="ZipArchiveEntry.ExternalAttributes"/> writes the mode bits, but some
/// framework/host combinations still mark the entry as DOS/FAT. Unix extractors
/// ignore Unix mode bits on those entries, so both fields must be corrected after
/// the archive has been closed.
/// </remarks>
internal static class ZipArchiveUnixPermissionPatcher
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064B50;
    private const uint CentralDirectoryEntrySignature = 0x02014B50;
    private const uint UnixExecutableExternalAttributes = 0x81ED0000;
    private const byte UnixHostSystem = 3;
    private const int EndOfCentralDirectoryMinimumLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;

    /// <summary>
    /// Marks the selected archive entries as Unix regular files with mode 0755.
    /// </summary>
    internal static void ApplyExecutablePermissions(string archivePath, IReadOnlyCollection<string> entryNames)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path is required.", nameof(archivePath));
        if (entryNames is null)
            throw new ArgumentNullException(nameof(entryNames));

        var requestedNames = new HashSet<string>(
            entryNames.Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.Ordinal);
        if (requestedNames.Count == 0)
            return;

        var selectedIndexes = ResolveEntryIndexes(archivePath, requestedNames);
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var directory = ReadCentralDirectory(stream);
        if ((ulong)selectedIndexes.Max() >= directory.EntryCount)
            throw new InvalidDataException($"ZIP central directory in '{archivePath}' does not contain the selected executable entry.");

        stream.Position = directory.Offset;
        for (ulong index = 0; index < directory.EntryCount; index++)
        {
            var entryOffset = stream.Position;
            var header = ReadExactly(stream, 46);
            if (ReadUInt32(header, 0) != CentralDirectoryEntrySignature)
                throw new InvalidDataException($"Invalid ZIP central-directory entry at offset {entryOffset} in '{archivePath}'.");

            if (selectedIndexes.Contains((int)index))
            {
                stream.Position = entryOffset + 5;
                stream.WriteByte(UnixHostSystem);
                stream.Position = entryOffset + 38;
                WriteUInt32(stream, UnixExecutableExternalAttributes);
            }

            var nameLength = ReadUInt16(header, 28);
            var extraLength = ReadUInt16(header, 30);
            var commentLength = ReadUInt16(header, 32);
            stream.Position = entryOffset + 46L + nameLength + extraLength + commentLength;
        }
    }

    private static HashSet<int> ResolveEntryIndexes(string archivePath, HashSet<string> requestedNames)
    {
        var selectedIndexes = new HashSet<int>();
        var foundNames = new HashSet<string>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            for (var index = 0; index < archive.Entries.Count; index++)
            {
                var entryName = archive.Entries[index].FullName;
                if (requestedNames.Contains(entryName))
                {
                    selectedIndexes.Add(index);
                    foundNames.Add(entryName);
                }
            }
        }

        if (selectedIndexes.Count != requestedNames.Count)
        {
            var missing = requestedNames
                .Where(name => !foundNames.Contains(name))
                .ToArray();
            throw new InvalidOperationException(
                $"Executable entr{(missing.Length == 1 ? "y" : "ies")} '{string.Join("', '", missing)}' " +
                $"{(missing.Length == 1 ? "was" : "were")} not found in archive '{archivePath}'.");
        }

        return selectedIndexes;
    }

    private static CentralDirectoryLocation ReadCentralDirectory(FileStream stream)
    {
        if (stream.Length < EndOfCentralDirectoryMinimumLength)
            throw new InvalidDataException("ZIP archive is too small to contain an end-of-central-directory record.");

        var searchLength = (int)Math.Min(
            stream.Length,
            EndOfCentralDirectoryMinimumLength + MaximumZipCommentLength);
        var searchOffset = stream.Length - searchLength;
        stream.Position = searchOffset;
        var tail = ReadExactly(stream, searchLength);

        var relativeEocdOffset = FindEndOfCentralDirectory(tail);
        if (relativeEocdOffset < 0 || relativeEocdOffset + EndOfCentralDirectoryMinimumLength > tail.Length)
            throw new InvalidDataException("ZIP end-of-central-directory record was not found.");

        var eocdOffset = searchOffset + relativeEocdOffset;
        var entryCount = ReadUInt16(tail, relativeEocdOffset + 10);
        var centralDirectoryOffset = ReadUInt32(tail, relativeEocdOffset + 16);
        if (entryCount != ushort.MaxValue && centralDirectoryOffset != uint.MaxValue)
            return new CentralDirectoryLocation(entryCount, centralDirectoryOffset);

        return ReadZip64CentralDirectory(stream, eocdOffset);
    }

    private static CentralDirectoryLocation ReadZip64CentralDirectory(FileStream stream, long eocdOffset)
    {
        var locatorOffset = eocdOffset - 20;
        if (locatorOffset < 0)
            throw new InvalidDataException("ZIP64 end-of-central-directory locator was not found.");

        stream.Position = locatorOffset;
        var locator = ReadExactly(stream, 20);
        if (ReadUInt32(locator, 0) != Zip64EndOfCentralDirectoryLocatorSignature)
            throw new InvalidDataException("ZIP64 end-of-central-directory locator was not found.");

        var zip64Offset = ReadUInt64(locator, 8);
        if (zip64Offset > long.MaxValue)
            throw new InvalidDataException("ZIP64 end-of-central-directory offset exceeds the supported file range.");

        stream.Position = (long)zip64Offset;
        var record = ReadExactly(stream, 56);
        if (ReadUInt32(record, 0) != Zip64EndOfCentralDirectorySignature)
            throw new InvalidDataException("ZIP64 end-of-central-directory record was not found.");

        var entryCount = ReadUInt64(record, 32);
        var centralDirectoryOffset = ReadUInt64(record, 48);
        if (centralDirectoryOffset > long.MaxValue)
            throw new InvalidDataException("ZIP64 central-directory offset exceeds the supported file range.");

        return new CentralDirectoryLocation(entryCount, (long)centralDirectoryOffset);
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of ZIP archive.");
            offset += read;
        }

        return buffer;
    }

    private static int FindEndOfCentralDirectory(byte[] buffer)
    {
        for (var index = buffer.Length - 4; index >= 0; index--)
        {
            if (ReadUInt32(buffer, index) != EndOfCentralDirectorySignature ||
                index + EndOfCentralDirectoryMinimumLength > buffer.Length)
            {
                continue;
            }

            var commentLength = ReadUInt16(buffer, index + 20);
            if (index + EndOfCentralDirectoryMinimumLength + commentLength == buffer.Length)
                return index;
        }

        return -1;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
        => (ushort)(buffer[offset] | buffer[offset + 1] << 8);

    private static uint ReadUInt32(byte[] buffer, int offset)
        => (uint)(buffer[offset]
                  | buffer[offset + 1] << 8
                  | buffer[offset + 2] << 16
                  | buffer[offset + 3] << 24);

    private static ulong ReadUInt64(byte[] buffer, int offset)
        => ReadUInt32(buffer, offset) | (ulong)ReadUInt32(buffer, offset + 4) << 32;

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private sealed class CentralDirectoryLocation
    {
        internal CentralDirectoryLocation(ulong entryCount, long offset)
        {
            EntryCount = entryCount;
            Offset = offset;
        }

        internal ulong EntryCount { get; }

        internal long Offset { get; }
    }
}
