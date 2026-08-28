using System.IO.Compression;
using System.Reflection;
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

    internal static PowerShellCompilationDependencyClosure Verify(PowerShellStrictDependencyClosureRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        PowerShellCompilationDependencyLockHasher.EnsureValid(request.DependencyGraph, nameof(request.DependencyGraph));
        var result = new PowerShellCompilationDependencyClosure
        {
            TargetFramework = request.TargetFramework,
            RuntimeIdentifier = request.RuntimeIdentifier
        };
        var targetRuntimeAssemblies = PowerShellTargetRuntimeAssemblyCatalog.ReadStableKeys(request.TargetFramework)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managedAssemblies = new List<ManagedAssemblyInspection>();
        foreach (var file in request.Files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
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
                var bytes = File.ReadAllBytes(file.Path);
                using var stream = new MemoryStream(bytes, writable: false);
                using var pe = new PEReader(stream);
                if (pe.HasMetadata)
                {
                    VerifyManagedAssembly(pe, file.Path, managedAssemblies, ComputeSha256(file.Path));
                    result.ManagedAssemblies++;
                }
                else
                {
                    result.NativeLibraries++;
                    result.Limitations.Add(
                        $"Native library certification remains fail-closed until Milestone 14 proves the exact target-host architecture, ABI, publisher, transitive native closure, deployment, and execution contract: {Path.GetFileName(file.Path)}");
                }
                continue;
            }

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(extension))
            {
                if (!VerifyExecutable(file.Path, result, managedAssemblies))
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

        VerifyManagedReferenceClosure(managedAssemblies, targetRuntimeAssemblies);
        VerifyReviewedDependencyGraph(request, managedAssemblies, targetRuntimeAssemblies);
        VerifyNativeReferenceClosure(managedAssemblies);
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

    private static bool VerifyExecutable(
        string path,
        PowerShellCompilationDependencyClosure result,
        ICollection<ManagedAssemblyInspection> managedAssemblies)
    {
        using var stream = File.OpenRead(path);
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (pe.HasMetadata)
            {
                VerifyManagedAssembly(pe, path, managedAssemblies, ComputeSha256(path));
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
            VerifyBundleEntry(stream, entry, path, result, managedAssemblies);
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
        PowerShellCompilationDependencyClosure result,
        ICollection<ManagedAssemblyInspection> managedAssemblies)
    {
        if (IsPowerShellSource(entry.RelativePath))
            throw new InvalidOperationException($"Strict runtime-free bundle '{bundlePath}' contains PowerShell source '{entry.RelativePath}'.");
        var bytes = ReadBundleEntry(stream, entry);
        switch (entry.Type)
        {
            case DotNetBundleFileType.Assembly:
                VerifyManagedAssembly(bytes, bundlePath + "!" + entry.RelativePath, managedAssemblies);
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

    private static void VerifyManagedAssembly(
        byte[] bytes,
        string displayPath,
        ICollection<ManagedAssemblyInspection> managedAssemblies)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            throw new InvalidDataException($"Expected managed assembly '{displayPath}' does not contain CLR metadata.");
        VerifyManagedAssembly(pe, displayPath, managedAssemblies, ComputeSha256(bytes));
    }

    private static void VerifyManagedAssembly(
        PEReader pe,
        string displayPath,
        ICollection<ManagedAssemblyInspection> managedAssemblies,
        string contentSha256)
    {
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly)
            throw new InvalidDataException($"Managed dependency '{displayPath}' is a netmodule without an independently lockable assembly identity.");
        var definition = reader.GetAssemblyDefinition();
        var assemblyIdentity = CreateAssemblyIdentity(
            reader.GetString(definition.Name),
            definition.Version,
            reader.GetBlobBytes(definition.PublicKey),
            publicKey: true,
            definition.Culture.IsNil ? string.Empty : reader.GetString(definition.Culture));
        var references = new List<AssemblyIdentity>();
        foreach (var referenceHandle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(referenceHandle);
            var name = reader.GetString(reference.Name);
            references.Add(CreateAssemblyIdentity(
                name,
                reference.Version,
                reader.GetBlobBytes(reference.PublicKeyOrToken),
                (reference.Flags & AssemblyFlags.PublicKey) != 0,
                reference.Culture.IsNil ? string.Empty : reader.GetString(reference.Culture)));
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
        var nativeImports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
            var import = method.GetImport();
            if (import.Module.IsNil) continue;
            var module = reader.GetModuleReference(import.Module);
            var name = reader.GetString(module.Name);
            if (!string.IsNullOrWhiteSpace(name)) nativeImports.Add(name);
        }
        managedAssemblies.Add(new ManagedAssemblyInspection(assemblyIdentity, displayPath, contentSha256, references.ToArray(), nativeImports.ToArray()));
    }

    private static void VerifyManagedReferenceClosure(
        IReadOnlyCollection<ManagedAssemblyInspection> assemblies,
        ISet<string> targetRuntimeAssemblies)
    {
        var delivered = assemblies.Select(static assembly => assembly.Identity.StableKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in assemblies)
        foreach (var reference in assembly.References.GroupBy(static reference => reference.StableKey, StringComparer.OrdinalIgnoreCase).Select(static group => group.First()))
        {
            if (delivered.Contains(reference.StableKey) || targetRuntimeAssemblies.Contains(reference.StableKey)) continue;
            throw new InvalidOperationException(
                $"Strict runtime-free managed dependency '{assembly.DisplayPath}' references missing managed assembly '{reference.DisplayName}'. The delivered closure must contain that exact assembly identity or classify its signed identity as part of the target runtime.");
        }
    }

    private static void VerifyReviewedDependencyGraph(
        PowerShellStrictDependencyClosureRequest request,
        IEnumerable<ManagedAssemblyInspection> assemblies,
        ISet<string> targetRuntimeAssemblies)
    {
        var locked = request.DependencyGraph.Nodes
            .Where(static node => node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment))
            .Where(static node => node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule)
            .Where(static node => node.Exists)
            .Where(static node => node.Disposition is PowerShellCompilationDependencyGraphDisposition.Referenced or
                PowerShellCompilationDependencyGraphDisposition.Bundled or
                PowerShellCompilationDependencyGraphDisposition.PrivateRestored)
            .Where(static node => Version.TryParse(node.Identity.Version, out _))
            .GroupBy(
                static node => PowerShellTargetRuntimeAssemblyCatalog.CreateStableKey(
                    node.Identity.Name,
                    Version.Parse(node.Identity.Version),
                    node.Identity.PublicKeyToken,
                    node.Identity.Culture),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in assemblies)
        {
            if (targetRuntimeAssemblies.Contains(assembly.Identity.StableKey) ||
                IsCompilerOwnedPrimaryAssembly(request.Files, assembly.DisplayPath))
                continue;
            if (!locked.TryGetValue(assembly.Identity.StableKey, out var candidates))
                throw new InvalidOperationException(
                    $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' is absent from the reviewed dependency graph.");
            var contentHashes = candidates.Select(static node => node.Identity.Sha256)
                .Where(static hash => !string.IsNullOrWhiteSpace(hash))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (contentHashes.Length == 0)
                throw new InvalidOperationException(
                    $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' has no SHA-256 content identity in the reviewed dependency graph.");
            if (!contentHashes.Contains(assembly.ContentSha256, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' does not match the SHA-256 content identity in the reviewed dependency graph.");
        }
    }

    private static bool IsCompilerOwnedPrimaryAssembly(
        IEnumerable<PowerShellCompilationArtifactFile> files,
        string displayPath)
    {
        foreach (var file in files.Where(static file => file.Role is "Primary" or "GeneratedAssembly" or "TypedAssembly"))
        {
            if (PowerShellCompilationPathSafety.PathEquals(file.Path, displayPath)) return true;
            var separator = displayPath.IndexOf('!');
            if (separator <= 0 || !PowerShellCompilationPathSafety.PathEquals(file.Path, displayPath.Substring(0, separator))) continue;
            var entryName = Path.GetFileNameWithoutExtension(displayPath.Substring(separator + 1));
            var primaryName = Path.GetFileNameWithoutExtension(file.Path);
            if (entryName.Equals(primaryName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void VerifyNativeReferenceClosure(IEnumerable<ManagedAssemblyInspection> assemblies)
    {
        foreach (var assembly in assemblies)
        foreach (var import in assembly.NativeImports.Distinct(StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Strict runtime-free managed dependency '{assembly.DisplayPath}' imports native library '{import}'. " +
                "Native dependency certification remains fail-closed until Milestone 14 records and validates the exact target-host architecture, ABI, publisher, transitive native closure, and execution proof.");
    }

    private static AssemblyIdentity CreateAssemblyIdentity(
        string name,
        Version version,
        byte[] publicKeyOrToken,
        bool publicKey,
        string culture)
    {
        var token = publicKey && publicKeyOrToken.Length > 0
            ? PowerShellTargetRuntimeAssemblyCatalog.ComputePublicKeyToken(publicKeyOrToken)
            : string.Concat(publicKeyOrToken.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return new AssemblyIdentity(name, version, token, culture);
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

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
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

    private sealed class ManagedAssemblyInspection
    {
        internal ManagedAssemblyInspection(AssemblyIdentity identity, string displayPath, string contentSha256, AssemblyIdentity[] references, string[] nativeImports)
        {
            Identity = identity;
            DisplayPath = displayPath;
            ContentSha256 = contentSha256;
            References = references;
            NativeImports = nativeImports;
        }

        internal AssemblyIdentity Identity { get; }
        internal string DisplayPath { get; }
        internal string ContentSha256 { get; }
        internal AssemblyIdentity[] References { get; }
        internal string[] NativeImports { get; }
    }

    private sealed class AssemblyIdentity
    {
        internal AssemblyIdentity(string name, Version version, string publicKeyToken, string culture)
        {
            Name = name;
            Version = version;
            PublicKeyToken = publicKeyToken;
            Culture = PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(culture);
        }

        internal string Name { get; }
        internal Version Version { get; }
        internal string PublicKeyToken { get; }
        internal string Culture { get; }
        internal string StableKey => PowerShellTargetRuntimeAssemblyCatalog.CreateStableKey(Name, Version, PublicKeyToken, Culture);
        internal string DisplayName => $"{Name}, Version={Version}, Culture={Culture}, PublicKeyToken={(PublicKeyToken.Length == 0 ? "null" : PublicKeyToken)}";
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
