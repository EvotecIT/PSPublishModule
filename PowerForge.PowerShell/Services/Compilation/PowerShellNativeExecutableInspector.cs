using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Reads native executable headers and import tables without loading target code.</summary>
internal static class PowerShellNativeExecutableInspector
{
    internal static PowerShellCompilationNativeExecutableEvidence Inspect(string path, string runtimeIdentifier)
    {
        var bytes = File.ReadAllBytes(path);
        PowerShellCompilationNativeExecutableEvidence evidence;
        if (bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z')
            evidence = InspectPe(bytes);
        else if (bytes.Length >= 4 && bytes[0] == 0x7f && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
            evidence = InspectElf(bytes);
        else if (bytes.Length >= 4 && ReadUInt32(bytes, 0) == 0xfeedfacf)
            evidence = InspectMachO(bytes);
        else
            throw new InvalidDataException($"Native executable '{path}' does not use a supported PE, ELF, or 64-bit Mach-O container.");
        EnsureArchitecture(runtimeIdentifier, evidence.Architecture);
        evidence.Sha256 = ComputeSha256(bytes);
        return evidence;
    }

    private static PowerShellCompilationNativeExecutableEvidence InspectPe(byte[] bytes)
    {
        var peOffset = ReadInt32(bytes, 0x3c);
        EnsureRange(bytes, peOffset, 24, "PE header");
        if (ReadUInt32(bytes, peOffset) != 0x00004550) throw new InvalidDataException("Native PE signature is invalid.");
        var machine = ReadUInt16(bytes, peOffset + 4);
        var sectionCount = ReadUInt16(bytes, peOffset + 6);
        var optionalSize = ReadUInt16(bytes, peOffset + 20);
        var optional = peOffset + 24;
        EnsureRange(bytes, optional, optionalSize, "PE optional header");
        var magic = ReadUInt16(bytes, optional);
        var dataDirectory = optional + (magic == 0x20b ? 112 : magic == 0x10b ? 96 : throw new InvalidDataException("Native PE optional-header format is unsupported."));
        EnsureRange(bytes, dataDirectory, 16 * 8, "PE data directories");
        var sectionOffset = optional + optionalSize;
        EnsureRange(bytes, sectionOffset, checked(sectionCount * 40), "PE section table");
        var sections = new List<PeSection>(sectionCount);
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionOffset + index * 40;
            sections.Add(new PeSection(
                ReadUInt32(bytes, offset + 12),
                Math.Max(ReadUInt32(bytes, offset + 8), ReadUInt32(bytes, offset + 16)),
                ReadUInt32(bytes, offset + 20)));
        }
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadPeImports(bytes, sections, ReadUInt32(bytes, dataDirectory + 8), imports);
        ReadPeDelayImports(bytes, sections, ReadUInt32(bytes, dataDirectory + 13 * 8), imports);
        return new PowerShellCompilationNativeExecutableEvidence
        {
            Format = "PE",
            Architecture = machine switch
            {
                0x014c => "x86",
                0x8664 => "x64",
                0xaa64 => "arm64",
                _ => "machine-0x" + machine.ToString("x4", System.Globalization.CultureInfo.InvariantCulture)
            },
            ImportedLibraries = imports.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static void ReadPeImports(byte[] bytes, IReadOnlyCollection<PeSection> sections, uint directoryRva, ISet<string> imports)
    {
        if (directoryRva == 0) return;
        var offset = MapPeRva(bytes, sections, directoryRva);
        for (var count = 0; count < 65_536; count++, offset += 20)
        {
            EnsureRange(bytes, offset, 20, "PE import descriptor");
            if (ReadUInt32(bytes, offset) == 0 && ReadUInt32(bytes, offset + 12) == 0 && ReadUInt32(bytes, offset + 16) == 0) return;
            imports.Add(ReadAscii(bytes, MapPeRva(bytes, sections, ReadUInt32(bytes, offset + 12))));
        }
        throw new InvalidDataException("Native PE import table did not terminate.");
    }

    private static void ReadPeDelayImports(byte[] bytes, IReadOnlyCollection<PeSection> sections, uint directoryRva, ISet<string> imports)
    {
        if (directoryRva == 0) return;
        var offset = MapPeRva(bytes, sections, directoryRva);
        for (var count = 0; count < 65_536; count++, offset += 32)
        {
            EnsureRange(bytes, offset, 32, "PE delay-import descriptor");
            var attributes = ReadUInt32(bytes, offset);
            var name = ReadUInt32(bytes, offset + 4);
            if (attributes == 0 && name == 0) return;
            if ((attributes & 1) == 0) throw new InvalidDataException("Native PE delay-import uses an unsupported virtual-address name.");
            imports.Add(ReadAscii(bytes, MapPeRva(bytes, sections, name)));
        }
        throw new InvalidDataException("Native PE delay-import table did not terminate.");
    }

    private static int MapPeRva(byte[] bytes, IEnumerable<PeSection> sections, uint rva)
    {
        foreach (var section in sections)
        {
            if (rva < section.VirtualAddress || rva - section.VirtualAddress >= section.VirtualSize) continue;
            var offset = checked((int)(section.RawOffset + rva - section.VirtualAddress));
            EnsureRange(bytes, offset, 1, "PE RVA");
            return offset;
        }
        throw new InvalidDataException($"Native PE RVA 0x{rva:x8} is outside the section table.");
    }

    private static PowerShellCompilationNativeExecutableEvidence InspectElf(byte[] bytes)
    {
        if (bytes[4] != 2 || bytes[5] != 1) throw new InvalidDataException("Only little-endian ELF64 NativeAOT output is supported.");
        var machine = ReadUInt16(bytes, 18);
        var programOffset = ReadUInt64(bytes, 32);
        var programEntrySize = ReadUInt16(bytes, 54);
        var programCount = ReadUInt16(bytes, 56);
        if (programEntrySize < 56) throw new InvalidDataException("ELF64 program-header size is invalid.");
        EnsureRange(bytes, CheckedInt(programOffset), checked(programEntrySize * programCount), "ELF program headers");
        var loads = new List<ElfLoad>();
        ulong dynamicOffset = 0;
        ulong dynamicSize = 0;
        for (var index = 0; index < programCount; index++)
        {
            var offset = CheckedInt(programOffset + (ulong)(index * programEntrySize));
            var type = ReadUInt32(bytes, offset);
            var fileOffset = ReadUInt64(bytes, offset + 8);
            var virtualAddress = ReadUInt64(bytes, offset + 16);
            var fileSize = ReadUInt64(bytes, offset + 32);
            var memorySize = ReadUInt64(bytes, offset + 40);
            if (type == 1) loads.Add(new ElfLoad(virtualAddress, Math.Max(fileSize, memorySize), fileOffset));
            if (type == 2) { dynamicOffset = fileOffset; dynamicSize = fileSize; }
        }
        if (dynamicOffset == 0 || dynamicSize == 0) throw new InvalidDataException("ELF64 executable has no dynamic dependency table.");
        EnsureRange(bytes, CheckedInt(dynamicOffset), CheckedInt(dynamicSize), "ELF dynamic table");
        var needed = new List<ulong>();
        ulong stringAddress = 0;
        ulong stringSize = 0;
        for (ulong cursor = 0; cursor + 16 <= dynamicSize; cursor += 16)
        {
            var offset = CheckedInt(dynamicOffset + cursor);
            var tag = ReadUInt64(bytes, offset);
            var value = ReadUInt64(bytes, offset + 8);
            if (tag == 0) break;
            if (tag == 1) needed.Add(value);
            else if (tag == 5) stringAddress = value;
            else if (tag == 10) stringSize = value;
        }
        if (stringAddress == 0 || stringSize == 0) throw new InvalidDataException("ELF64 dynamic string table is missing.");
        var stringOffset = MapElfAddress(bytes, loads, stringAddress);
        foreach (var index in needed)
            if (index >= stringSize)
                throw new InvalidDataException("ELF64 dependency string offset is outside the dynamic string table.");
        var imports = needed.Select(index => ReadAscii(bytes, checked(stringOffset + CheckedInt(index)), CheckedInt(stringSize - index)))
            .Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        return new PowerShellCompilationNativeExecutableEvidence
        {
            Format = "ELF",
            Architecture = machine switch { 62 => "x64", 183 => "arm64", _ => "machine-" + machine },
            ImportedLibraries = imports
        };
    }

    private static int MapElfAddress(byte[] bytes, IEnumerable<ElfLoad> loads, ulong address)
    {
        foreach (var load in loads)
        {
            if (address < load.VirtualAddress || address - load.VirtualAddress >= load.Size) continue;
            var offset = CheckedInt(load.FileOffset + address - load.VirtualAddress);
            EnsureRange(bytes, offset, 1, "ELF virtual address");
            return offset;
        }
        throw new InvalidDataException("ELF64 dynamic string address is outside loadable segments.");
    }

    private static PowerShellCompilationNativeExecutableEvidence InspectMachO(byte[] bytes)
    {
        EnsureRange(bytes, 0, 32, "Mach-O header");
        var cpu = ReadUInt32(bytes, 4);
        var commandCount = ReadUInt32(bytes, 16);
        var commandBytes = ReadUInt32(bytes, 20);
        EnsureRange(bytes, 32, CheckedInt(commandBytes), "Mach-O load commands");
        var imports = new HashSet<string>(StringComparer.Ordinal);
        var offset = 32;
        for (uint index = 0; index < commandCount; index++)
        {
            EnsureRange(bytes, offset, 8, "Mach-O load command");
            var command = ReadUInt32(bytes, offset);
            var size = ReadUInt32(bytes, offset + 4);
            if (size < 8) throw new InvalidDataException("Mach-O load-command size is invalid.");
            EnsureRange(bytes, offset, CheckedInt(size), "Mach-O load command");
            if (command is 0x0c or 0x18 or 0x1f or 0x80000018 or 0x8000001f or 0x80000023)
            {
                var nameOffset = ReadUInt32(bytes, offset + 8);
                if (nameOffset >= size) throw new InvalidDataException("Mach-O dylib name offset is invalid.");
                imports.Add(ReadAscii(bytes, checked(offset + CheckedInt(nameOffset)), CheckedInt(size - nameOffset)));
            }
            offset = checked(offset + CheckedInt(size));
        }
        return new PowerShellCompilationNativeExecutableEvidence
        {
            Format = "MachO",
            Architecture = cpu switch { 0x01000007 => "x64", 0x0100000c => "arm64", _ => "cpu-0x" + cpu.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) },
            ImportedLibraries = imports.OrderBy(static name => name, StringComparer.Ordinal).ToArray()
        };
    }

    private static void EnsureArchitecture(string runtimeIdentifier, string architecture)
    {
        var expected = runtimeIdentifier.Substring(runtimeIdentifier.LastIndexOf('-') + 1);
        if (!expected.Equals(architecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Native executable architecture '{architecture}' does not match runtime identifier '{runtimeIdentifier}'.");
    }

    private static string ReadAscii(byte[] bytes, int offset, int maximum = 4096)
    {
        EnsureRange(bytes, offset, 1, "native string");
        var end = offset;
        var limit = Math.Min(bytes.Length, checked(offset + maximum));
        while (end < limit && bytes[end] != 0) end++;
        if (end == limit) throw new InvalidDataException("Native dependency string is unterminated.");
        return Encoding.ASCII.GetString(bytes, offset, end - offset);
    }

    private static void EnsureRange(byte[] bytes, int offset, int length, string owner)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"{owner} points outside the native executable.");
    }

    private static int CheckedInt(ulong value)
        => value > int.MaxValue ? throw new InvalidDataException("Native executable offset exceeds the supported inspection size.") : (int)value;

    private static ushort ReadUInt16(byte[] bytes, int offset) { EnsureRange(bytes, offset, 2, "native integer"); return BitConverter.ToUInt16(bytes, offset); }
    private static int ReadInt32(byte[] bytes, int offset) { EnsureRange(bytes, offset, 4, "native integer"); return BitConverter.ToInt32(bytes, offset); }
    private static uint ReadUInt32(byte[] bytes, int offset) { EnsureRange(bytes, offset, 4, "native integer"); return BitConverter.ToUInt32(bytes, offset); }
    private static ulong ReadUInt64(byte[] bytes, int offset) { EnsureRange(bytes, offset, 8, "native integer"); return BitConverter.ToUInt64(bytes, offset); }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private sealed class PeSection
    {
        internal PeSection(uint virtualAddress, uint virtualSize, uint rawOffset) { VirtualAddress = virtualAddress; VirtualSize = virtualSize; RawOffset = rawOffset; }
        internal uint VirtualAddress { get; }
        internal uint VirtualSize { get; }
        internal uint RawOffset { get; }
    }

    private sealed class ElfLoad
    {
        internal ElfLoad(ulong virtualAddress, ulong size, ulong fileOffset) { VirtualAddress = virtualAddress; Size = size; FileOffset = fileOffset; }
        internal ulong VirtualAddress { get; }
        internal ulong Size { get; }
        internal ulong FileOffset { get; }
    }
}
