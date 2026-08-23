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
        NormalizeGuidHeap(metadata);
        ZeroDirectory(image, reader.PEHeaders, corHeader.StrongNameSignatureDirectory);
        ZeroDirectory(image, reader.PEHeaders, peHeader.ImportAddressTableDirectory);

        int sectionStart = reader.PEHeaders.SectionHeaders.Min(section => section.PointerToRawData);
        int metadataStart = reader.PEHeaders.MetadataStartOffset;
        if (sectionStart < 0 || metadataStart < sectionStart || metadataStart > image.Length)
            throw new InvalidDataException("The managed PE section layout is invalid.");

        using var content = new MemoryStream();
        WriteInt32(content, (int)reader.PEHeaders.CoffHeader.Machine);
        WriteInt32(content, (int)reader.PEHeaders.CoffHeader.Characteristics);
        WriteInt32(content, (int)peHeader.Magic);
        WriteInt32(content, (int)peHeader.Subsystem);
        WriteInt32(content, (int)peHeader.DllCharacteristics);
        WriteInt32(content, (int)corHeader.Flags);
        WriteInt32(content, corHeader.EntryPointTokenOrRelativeVirtualAddress);
        content.Write(image, sectionStart, metadataStart - sectionStart);
        content.Write(metadata, 0, metadata.Length);
        WriteDirectoryContent(content, image, reader.PEHeaders, corHeader.ResourcesDirectory);
        WriteDirectoryContent(content, image, reader.PEHeaders, peHeader.ResourceTableDirectory);
        return content.ToArray();
    }

    private static void NormalizeGuidHeap(byte[] metadata)
    {
        if (metadata.Length < 20 || BitConverter.ToUInt32(metadata, 0) != 0x424A5342)
            throw new InvalidDataException("The managed metadata root is invalid.");

        int versionLength = checked((int)BitConverter.ToUInt32(metadata, 12));
        int position = AlignToFour(16 + versionLength);
        if (position + 4 > metadata.Length)
            throw new InvalidDataException("The managed metadata stream table is truncated.");

        ushort streamCount = BitConverter.ToUInt16(metadata, position + 2);
        position += 4;
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
            ZeroRange(metadata, streamOffset, streamSize);
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

    private static void WriteDirectoryContent(
        Stream destination,
        byte[] image,
        PEHeaders headers,
        DirectoryEntry directory)
    {
        WriteInt32(destination, directory.Size);
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
            return;
        int offset = MapRvaToFileOffset(headers, directory.RelativeVirtualAddress);
        if (offset < 0 || directory.Size < 0 || offset > image.Length - directory.Size)
            throw new InvalidDataException("The PE directory is outside the image.");
        destination.Write(image, offset, directory.Size);
    }

    private static void WriteInt32(Stream destination, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        destination.Write(bytes, 0, bytes.Length);
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
