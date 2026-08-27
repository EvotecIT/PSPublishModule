using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Mechanically inspects the delivered Strict artifact set for PowerShell source,
/// PowerShell runtime references, and known dynamic execution entry points.
/// </summary>
internal static class PowerShellStrictDependencyClosureVerifier
{
    private static readonly byte[] BundleSignature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    };

    private static readonly string[] ForbiddenSourceTokens =
    {
        "System.Management.Automation",
        "ScriptBlock.Create",
        "PowerShell.Create"
    };

    private static readonly HashSet<string> EvidenceRoles = new(StringComparer.Ordinal)
    {
        "DebugSymbols",
        "ExternalHelp",
        "GeneratedBuildIsolation",
        "GeneratedProject",
        "GeneratedSource",
        "GeneratedSourceMap"
    };

    internal static PowerShellCompilationDependencyClosure Verify(IEnumerable<PowerShellCompilationArtifactFile> files)
    {
        var result = new PowerShellCompilationDependencyClosure();
        foreach (var file in files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            result.InspectedFiles++;
            VerifyRecordedFile(file);
            if (IsPowerShellSource(file.Path))
                throw new InvalidOperationException($"Strict runtime-free artifact contains PowerShell source '{file.Path}'.");

            var extension = Path.GetExtension(file.Path);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                VerifyGeneratedSource(file.Path);
                continue;
            }

            if (EvidenceRoles.Contains(file.Role))
                continue;

            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                VerifyManagedAssembly(File.ReadAllBytes(file.Path), file.Path);
                result.ManagedAssemblies++;
                continue;
            }

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(extension))
            {
                if (!VerifyExecutable(file.Path, result))
                    result.Limitations.Add($"Executable format is not currently certifiable: {Path.GetFileName(file.Path)}");
                continue;
            }

            if (file.Path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                VerifyDependencyManifest(File.ReadAllBytes(file.Path), file.Path);
                continue;
            }

            if (file.Path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;

            // Opaque resources are acceptable only when they are not executable dependencies.
            if (file.Role.Equals("RuntimeDependency", StringComparison.Ordinal))
                result.Limitations.Add($"Runtime dependency format is not currently certifiable: {Path.GetFileName(file.Path)}");
        }

        result.Verified = result.Limitations.Count == 0;
        return result;
    }

    internal static bool IsPowerShellSource(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyRecordedFile(PowerShellCompilationArtifactFile file)
    {
        if (!File.Exists(file.Path))
            throw new FileNotFoundException("Strict dependency-closure input was not found.", file.Path);
        var actualSize = new FileInfo(file.Path).Length;
        if (file.SizeBytes > 0 && file.SizeBytes != actualSize)
            throw new InvalidOperationException($"Strict dependency-closure input '{file.Path}' changed after artifact staging.");
        if (string.IsNullOrWhiteSpace(file.Sha256)) return;
        var actualHash = ComputeSha256(file.Path);
        if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Strict dependency-closure input '{file.Path}' does not match its recorded SHA-256.");
    }

    private static void VerifyGeneratedSource(string path)
    {
        var source = File.ReadAllText(path);
        foreach (var token in ForbiddenSourceTokens)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"Strict runtime-free generated source '{path}' contains forbidden PowerShell runtime token '{token}'.");
        }
    }

    private static bool VerifyExecutable(string path, PowerShellCompilationDependencyClosure result)
    {
        using var stream = File.OpenRead(path);
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (pe.HasMetadata)
            {
                VerifyManagedAssembly(pe, path);
                result.ManagedAssemblies++;
                return true;
            }
        }
        catch (BadImageFormatException)
        {
            // A .NET apphost or another platform executable is inspected as a bundle below.
        }

        stream.Position = 0;
        var headerOffset = FindBundleHeaderOffset(stream);
        if (headerOffset is null)
            return false;
        if (headerOffset == 0)
        {
            result.ArtifactFormat = "DotNetAppHost";
            return true;
        }
        var manifest = ReadBundleManifest(stream, headerOffset.Value, path);
        result.ArtifactFormat = $"DotNetSingleFile/{manifest.MajorVersion}.{manifest.MinorVersion}";
        result.BundledEntries += manifest.Entries.Count;
        foreach (var entry in manifest.Entries)
            VerifyBundleEntry(stream, entry, path, result);
        return true;
    }

    private static long? FindBundleHeaderOffset(Stream stream)
    {
        const int blockSize = 64 * 1024;
        var buffer = new byte[blockSize + BundleSignature.Length - 1];
        var retained = 0;
        long absoluteStart = 0;
        var foundSignature = false;
        while (true)
        {
            var read = stream.Read(buffer, retained, blockSize);
            if (read == 0) break;
            var available = retained + read;
            for (var index = 0; index <= available - BundleSignature.Length; index++)
            {
                if (!Matches(buffer, index, BundleSignature)) continue;
                foundSignature = true;
                var signatureOffset = absoluteStart - retained + index;
                if (signatureOffset < sizeof(long)) continue;
                var returnPosition = stream.Position;
                stream.Position = signatureOffset - sizeof(long);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                var headerOffset = reader.ReadInt64();
                stream.Position = returnPosition;
                if (headerOffset == 0)
                    return 0;
                if (IsBundleHeaderCandidate(stream, headerOffset, returnPosition))
                    return headerOffset;
            }

            retained = Math.Min(BundleSignature.Length - 1, available);
            Buffer.BlockCopy(buffer, available - retained, buffer, 0, retained);
            absoluteStart += read;
        }
        if (foundSignature)
            throw new InvalidDataException("The .NET bundle signature did not reference a valid manifest header.");
        return null;
    }

    private static bool IsBundleHeaderCandidate(Stream stream, long headerOffset, long returnPosition)
    {
        if (headerOffset <= 0 || headerOffset > stream.Length - 12)
            return false;
        try
        {
            stream.Position = headerOffset;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var major = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var count = reader.ReadInt32();
            return major is >= 1 and <= 6 && count is >= 0 and <= 100_000;
        }
        finally
        {
            stream.Position = returnPosition;
        }
    }

    private static DotNetBundleManifest ReadBundleManifest(Stream stream, long headerOffset, string path)
    {
        if (headerOffset <= 0 || headerOffset >= stream.Length)
            throw new InvalidDataException($"The .NET bundle header offset in '{path}' is outside the delivered file.");
        stream.Position = headerOffset;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var major = reader.ReadUInt32();
        var minor = reader.ReadUInt32();
        if (major is < 1 or > 6)
            throw new InvalidDataException($"The .NET bundle in '{path}' uses unsupported manifest version {major}.{minor}.");
        var count = reader.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new InvalidDataException($"The .NET bundle in '{path}' declares an invalid entry count.");
        _ = reader.ReadString();
        if (major >= 2)
        {
            _ = reader.ReadInt64();
            _ = reader.ReadInt64();
            _ = reader.ReadInt64();
            _ = reader.ReadInt64();
            _ = reader.ReadUInt64();
        }

        var entries = new List<DotNetBundleEntry>(count);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            var compressedSize = major >= 6 ? reader.ReadInt64() : 0;
            var type = (DotNetBundleFileType)reader.ReadByte();
            var relativePath = reader.ReadString();
            ValidateBundleEntry(path, headerOffset, offset, size, compressedSize, type, relativePath, paths);
            entries.Add(new DotNetBundleEntry(offset, size, compressedSize, type, relativePath));
        }
        return new DotNetBundleManifest(major, minor, entries);
    }

    private static void ValidateBundleEntry(
        string bundlePath,
        long headerOffset,
        long offset,
        long size,
        long compressedSize,
        DotNetBundleFileType type,
        string relativePath,
        ISet<string> paths)
    {
        var payloadSize = compressedSize > 0 ? compressedSize : size;
        if (offset < 0 || size < 0 || compressedSize < 0 || payloadSize > headerOffset || offset > headerOffset - payloadSize)
            throw new InvalidDataException($"The .NET bundle entry '{relativePath}' in '{bundlePath}' points outside the payload.");
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Split('/', '\\').Any(static part => part.Equals("..", StringComparison.Ordinal)))
            throw new InvalidDataException($"The .NET bundle in '{bundlePath}' contains unsafe entry path '{relativePath}'.");
        if (!paths.Add(relativePath))
            throw new InvalidDataException($"The .NET bundle in '{bundlePath}' contains duplicate entry path '{relativePath}'.");
        if (!Enum.IsDefined(typeof(DotNetBundleFileType), type) || type == DotNetBundleFileType.Unknown)
            throw new InvalidDataException($"The .NET bundle entry '{relativePath}' in '{bundlePath}' has an unsupported file type.");
    }

    private static void VerifyBundleEntry(
        Stream stream,
        DotNetBundleEntry entry,
        string bundlePath,
        PowerShellCompilationDependencyClosure result)
    {
        if (IsPowerShellSource(entry.RelativePath))
            throw new InvalidOperationException($"Strict runtime-free bundle '{bundlePath}' contains PowerShell source '{entry.RelativePath}'.");
        var bytes = ReadBundleEntry(stream, entry);
        switch (entry.Type)
        {
            case DotNetBundleFileType.Assembly:
                VerifyManagedAssembly(bytes, bundlePath + "!" + entry.RelativePath);
                result.ManagedAssemblies++;
                break;
            case DotNetBundleFileType.DepsJson:
                VerifyDependencyManifest(bytes, bundlePath + "!" + entry.RelativePath);
                break;
            case DotNetBundleFileType.NativeBinary:
                result.Limitations.Add($"Bundled native dependency is not currently certifiable: {entry.RelativePath}");
                break;
            case DotNetBundleFileType.RuntimeConfigJson:
            case DotNetBundleFileType.Symbols:
                break;
            default:
                throw new InvalidDataException($"The .NET bundle entry '{entry.RelativePath}' in '{bundlePath}' has an unsupported file type.");
        }
    }

    private static byte[] ReadBundleEntry(Stream stream, DotNetBundleEntry entry)
    {
        stream.Position = entry.Offset;
        var payloadLength = entry.CompressedSize > 0 ? entry.CompressedSize : entry.Size;
        if (payloadLength > int.MaxValue || entry.Size > int.MaxValue)
            throw new InvalidDataException($"The .NET bundle entry '{entry.RelativePath}' is too large to inspect safely.");
        var payload = ReadExactly(stream, (int)payloadLength);
        if (entry.CompressedSize == 0) return payload;
        using var compressed = new MemoryStream(payload, writable: false);
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var expanded = new MemoryStream((int)entry.Size);
        deflate.CopyTo(expanded);
        if (expanded.Length != entry.Size)
            throw new InvalidDataException($"The .NET bundle entry '{entry.RelativePath}' did not expand to its declared size.");
        return expanded.ToArray();
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException("Unexpected end of .NET bundle payload.");
            offset += read;
        }
        return bytes;
    }

    private static void VerifyDependencyManifest(byte[] bytes, string displayPath)
    {
        var json = Encoding.UTF8.GetString(bytes);
        if (json.IndexOf("System.Management.Automation", StringComparison.OrdinalIgnoreCase) >= 0 ||
            json.IndexOf("Microsoft.PowerShell.SDK", StringComparison.OrdinalIgnoreCase) >= 0)
            throw new InvalidOperationException($"Strict runtime-free dependency manifest '{displayPath}' contains a PowerShell runtime dependency.");
    }

    private static void VerifyManagedAssembly(byte[] bytes, string displayPath)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            throw new InvalidDataException($"Expected managed assembly '{displayPath}' does not contain CLR metadata.");
        VerifyManagedAssembly(pe, displayPath);
    }

    private static void VerifyManagedAssembly(PEReader pe, string displayPath)
    {
        var reader = pe.GetMetadataReader();
        foreach (var referenceHandle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(referenceHandle);
            var name = reader.GetString(reference.Name);
            if (name.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Microsoft.PowerShell.SDK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Strict runtime-free managed dependency '{displayPath}' references forbidden PowerShell assembly '{name}'.");
        }

        foreach (var memberHandle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(memberHandle);
            if (!reader.GetString(member.Name).Equals(nameof(System.Diagnostics.Process.Start), StringComparison.Ordinal) ||
                !IsProcessType(reader, member.Parent))
                continue;
            throw new InvalidOperationException($"Strict runtime-free managed dependency '{displayPath}' contains a native-process launch reference.");
        }
    }

    private static bool IsProcessType(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return reader.GetString(type.Namespace).Equals("System.Diagnostics", StringComparison.Ordinal) &&
                   reader.GetString(type.Name).Equals(nameof(System.Diagnostics.Process), StringComparison.Ordinal);
        }
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return reader.GetString(type.Namespace).Equals("System.Diagnostics", StringComparison.Ordinal) &&
                   reader.GetString(type.Name).Equals(nameof(System.Diagnostics.Process), StringComparison.Ordinal);
        }
        return false;
    }

    private static bool Matches(byte[] buffer, int offset, byte[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            if (buffer[offset + index] != expected[index]) return false;
        }
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private enum DotNetBundleFileType : byte
    {
        Unknown,
        Assembly,
        NativeBinary,
        DepsJson,
        RuntimeConfigJson,
        Symbols
    }

    private sealed class DotNetBundleManifest
    {
        internal DotNetBundleManifest(uint majorVersion, uint minorVersion, IReadOnlyList<DotNetBundleEntry> entries)
        {
            MajorVersion = majorVersion;
            MinorVersion = minorVersion;
            Entries = entries;
        }

        internal uint MajorVersion { get; }
        internal uint MinorVersion { get; }
        internal IReadOnlyList<DotNetBundleEntry> Entries { get; }
    }

    private sealed class DotNetBundleEntry
    {
        internal DotNetBundleEntry(long offset, long size, long compressedSize, DotNetBundleFileType type, string relativePath)
        {
            Offset = offset;
            Size = size;
            CompressedSize = compressedSize;
            Type = type;
            RelativePath = relativePath;
        }

        internal long Offset { get; }
        internal long Size { get; }
        internal long CompressedSize { get; }
        internal DotNetBundleFileType Type { get; }
        internal string RelativePath { get; }
    }
}
