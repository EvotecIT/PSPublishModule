using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool AreControlledGeneratedOutputsEquivalent(
        string candidatePath,
        string controlledPath)
    {
        byte[] candidateDigest = ComputeGeneratedOutputSha256(candidatePath);
        byte[] controlledDigest = ComputeGeneratedOutputSha256(controlledPath);
        if (candidateDigest.SequenceEqual(controlledDigest))
            return true;

        try
        {
            if (HasAuthenticodeCertificateTable(candidatePath) ||
                HasAuthenticodeCertificateTable(controlledPath) ||
                HasPeOverlay(candidatePath) ||
                HasPeOverlay(controlledPath))
            {
                return false;
            }

            byte[] candidateContent = ReadManagedProvenanceContent(candidatePath);
            byte[] controlledContent = ReadManagedProvenanceContent(controlledPath);
            return candidateContent.SequenceEqual(controlledContent);
        }
        catch
        {
            // Non-managed or malformed outputs require exact byte identity.
            return false;
        }
    }

    private static byte[] ComputeGeneratedOutputSha256(string path)
    {
        using SHA256 hash = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return hash.ComputeHash(stream);
    }

    private static bool HasAuthenticodeCertificateTable(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return reader.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0;
    }

    private static bool HasPeOverlay(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        int imageEnd = reader.PEHeaders.SectionHeaders.Max(section =>
            checked(section.PointerToRawData + section.SizeOfRawData));
        return stream.Length > imageEnd;
    }

    private static byte[] ReadManagedProvenanceContent(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (!reader.HasMetadata ||
            reader.PEHeaders.PEHeader is not PEHeader peHeader ||
            reader.PEHeaders.CorHeader is not CorHeader corHeader ||
            (corHeader.Flags & CorFlags.ILOnly) == 0 ||
            (corHeader.Flags & CorFlags.NativeEntryPoint) != 0 ||
            corHeader.ManagedNativeHeaderDirectory.Size != 0)
        {
            throw new InvalidDataException("The generated output is not an IL-only managed PE image.");
        }

        byte[] image = reader.GetEntireImage().GetContent().ToArray();
        byte[] metadata = reader.GetMetadata().GetContent().ToArray();
        MetadataReader metadataReader = reader.GetMetadataReader();
        Guid moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid);
        NormalizeNonIdentityGuidHeapEntries(metadata, moduleVersionId);
        int metadataStart = reader.PEHeaders.MetadataStartOffset;
        if (metadataStart < 0 || metadataStart > image.Length - metadata.Length)
            throw new InvalidDataException("The managed metadata is outside the PE image.");
        Buffer.BlockCopy(metadata, 0, image, metadataStart, metadata.Length);
        int peHeaderStart = reader.PEHeaders.PEHeaderStartOffset;
        ZeroRange(image, peHeaderStart - 16, sizeof(uint));
        ZeroRange(image, peHeaderStart + 64, sizeof(uint));
        ZeroDirectory(image, reader.PEHeaders, peHeader.ImportAddressTableDirectory);
        NormalizeDebugRecords(image, reader, peHeader.DebugTableDirectory);

        SectionHeader[] mappedSections = reader.PEHeaders.SectionHeaders
            .Where(section => section.SizeOfRawData > 0)
            .ToArray();
        if (mappedSections.Length == 0)
            throw new InvalidDataException("The managed PE image has no mapped section data.");
        int sectionStart = mappedSections.Min(section => section.PointerToRawData);
        int sectionEnd = mappedSections.Max(section =>
            checked(section.PointerToRawData + section.SizeOfRawData));
        if (sectionStart < 0 || sectionEnd < sectionStart || sectionEnd > image.Length)
            throw new InvalidDataException("The managed PE section layout is invalid.");

        // Compare the complete mapped image, including the DOS, COFF, optional,
        // and section headers. Only explicitly identified build-path/timestamp
        // fields above are normalized.
        return image.AsSpan(0, sectionEnd).ToArray();
    }

    private static void NormalizeDebugRecords(
        byte[] image,
        PEReader reader,
        DirectoryEntry debugDirectory)
    {
        DebugDirectoryEntry[] entries = reader.ReadDebugDirectory().ToArray();
        if (entries.Length == 0)
            return;
        if (debugDirectory.Size != checked(entries.Length * 28))
            throw new InvalidDataException("The PE debug directory size is invalid.");

        int tableOffset = MapRvaToFileOffset(
            reader.PEHeaders,
            debugDirectory.RelativeVirtualAddress);
        for (int index = 0; index < entries.Length; index++)
        {
            DebugDirectoryEntry entry = entries[index];
            // IMAGE_DEBUG_DIRECTORY.TimeDateStamp is non-semantic build time.
            ZeroRange(image, checked(tableOffset + (index * 28) + 4), sizeof(uint));
            if (entry.Type != DebugDirectoryEntryType.CodeView)
                continue;
            if (entry.DataSize < 24 || entry.DataPointer < 0 ||
                entry.DataPointer > image.Length - entry.DataSize)
            {
                throw new InvalidDataException("The CodeView debug record is outside the PE image.");
            }

            // Preserve the RSDS signature, PDB identity, and age. Only the
            // machine-local PDB path that follows them is normalized.
            ZeroRange(image, checked(entry.DataPointer + 24), entry.DataSize - 24);
        }
    }

    private static void NormalizeNonIdentityGuidHeapEntries(byte[] metadata, Guid moduleVersionId)
    {
        if (metadata.Length < 20 || BitConverter.ToUInt32(metadata, 0) != 0x424A5342)
            throw new InvalidDataException("The managed metadata root is invalid.");

        int versionLength = checked((int)BitConverter.ToUInt32(metadata, 12));
        int position = AlignToFour(16 + versionLength);
        if (position + 4 > metadata.Length)
            throw new InvalidDataException("The managed metadata stream table is truncated.");

        ushort streamCount = BitConverter.ToUInt16(metadata, position + 2);
        position += 4;
        byte[] moduleVersionIdBytes = moduleVersionId.ToByteArray();
        for (int index = 0; index < streamCount; index++)
        {
            if (position + 8 > metadata.Length)
                throw new InvalidDataException("The managed metadata stream header is truncated.");
            int streamOffset = checked((int)BitConverter.ToUInt32(metadata, position));
            int streamSize = checked((int)BitConverter.ToUInt32(metadata, position + 4));
            int nameStart = position + 8;
            int nameEnd = Array.IndexOf(metadata, (byte)0, nameStart);
            if (nameEnd < 0)
                throw new InvalidDataException("The managed metadata stream name is unterminated.");

            string name = Encoding.ASCII.GetString(metadata, nameStart, nameEnd - nameStart);
            position = AlignToFour(nameEnd + 1);
            if (!name.Equals("#GUID", StringComparison.Ordinal))
                continue;
            if (streamOffset < 0 || streamSize < 0 || streamOffset > metadata.Length - streamSize)
                throw new InvalidDataException("The managed GUID heap is outside the metadata image.");

            int streamEnd = streamOffset + streamSize;
            for (int guidOffset = streamOffset; guidOffset + 16 <= streamEnd; guidOffset += 16)
            {
                if (!metadata.AsSpan(guidOffset, 16).SequenceEqual(moduleVersionIdBytes))
                    ZeroRange(metadata, guidOffset, 16);
            }
            return;
        }
    }

    private static void ZeroDirectory(
        byte[] image,
        PEHeaders headers,
        DirectoryEntry directory)
    {
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
            return;
        int offset = MapRvaToFileOffset(headers, directory.RelativeVirtualAddress);
        ZeroRange(image, offset, directory.Size);
    }

    private static int MapRvaToFileOffset(PEHeaders headers, int relativeVirtualAddress)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (relativeVirtualAddress >= section.VirtualAddress &&
                relativeVirtualAddress < section.VirtualAddress + sectionSize)
            {
                return section.PointerToRawData + relativeVirtualAddress - section.VirtualAddress;
            }
        }
        throw new InvalidDataException("The PE directory is outside every section.");
    }

    private static void ZeroRange(byte[] bytes, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > bytes.Length - count)
            throw new InvalidDataException("The PE range is outside the image.");
        Array.Clear(bytes, offset, count);
    }

    private static int AlignToFour(int value) => checked((value + 3) & ~3);
}
