using System.Management.Automation.Language;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Builds a deterministic dependency lock graph without importing or executing source modules.</summary>
internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
    private readonly string _moduleRoot;
    private readonly PowerShellCompilationMode _mode;
    private readonly PowerShellCompilationArtifactKind _artifactKind;
    private readonly Dictionary<string, PowerShellCompilationDependencyNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<PowerShellCompilationDependencyEdge> _edges = new();
    private readonly Dictionary<string, string> _pathNodes = new(PowerShellCompilationPathSafety.PathComparer);

    private PowerShellCompilationDependencyGraphBuilder(
        string moduleRoot,
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind artifactKind)
    {
        _moduleRoot = Path.GetFullPath(moduleRoot);
        _mode = mode;
        _artifactKind = artifactKind;
    }

    internal static PowerShellCompilationDependencyGraph Build(
        string sourcePath,
        string? manifestPath,
        string moduleRoot,
        PowerShellCompilationArtifactKind artifactKind,
        PowerShellCompilationMode mode,
        IEnumerable<string> compilationSourceFiles,
        IReadOnlyCollection<PowerShellCompilationDependency> dependencies,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        bool includeRuntimePack = false)
    {
        var builder = new PowerShellCompilationDependencyGraphBuilder(moduleRoot, mode, artifactKind);
        return builder.BuildCore(
            sourcePath,
            manifestPath,
            compilationSourceFiles,
            dependencies,
            targetFramework,
            runtimeIdentifier,
            includeRuntimePack);
    }

    private PowerShellCompilationDependencyGraph BuildCore(
        string sourcePath,
        string? manifestPath,
        IEnumerable<string> compilationSourceFiles,
        IReadOnlyCollection<PowerShellCompilationDependency> dependencies,
        string? targetFramework,
        string? runtimeIdentifier,
        bool includeRuntimePack)
    {
        var sourceNodeId = AddLocalNode(
            sourcePath,
            ClassifyLocalPath(sourcePath),
            PowerShellCompilationDependencyGraphRole.Semantic |
            PowerShellCompilationDependencyGraphRole.Dependency |
            PowerShellCompilationDependencyGraphRole.Deployment,
            PowerShellCompilationDependencyGraphDisposition.Compiled,
            "Primary compilation input.",
            targetFramework,
            runtimeIdentifier);
        var rootId = !string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath)
            ? AddLocalNode(
                manifestPath!,
                PowerShellCompilationDependencyNodeKind.ModuleManifest,
                PowerShellCompilationDependencyGraphRole.Semantic |
                PowerShellCompilationDependencyGraphRole.Dependency |
                PowerShellCompilationDependencyGraphRole.Deployment,
                _artifactKind == PowerShellCompilationArtifactKind.BinaryModule
                    ? PowerShellCompilationDependencyGraphDisposition.Bundled
                    : PowerShellCompilationDependencyGraphDisposition.Referenced,
                "Primary module manifest input.",
                targetFramework,
                runtimeIdentifier)
            : sourceNodeId;

        foreach (var dependency in dependencies.OrderBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var nodeId = AddDependencyNode(dependency, targetFramework, runtimeIdentifier);
            if (nodeId == rootId) continue;
            AddEdge(rootId, nodeId, MapEdgeKind(dependency.Discovery), dependency.RelativePath);
        }

        foreach (var input in compilationSourceFiles
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PowerShellCompilationPathSafety.PathComparer)
                     .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer))
        {
            var inputId = AddLocalNode(
                input,
                ClassifyLocalPath(input),
                PowerShellCompilationDependencyGraphRole.Semantic |
                PowerShellCompilationDependencyGraphRole.Dependency |
                PowerShellCompilationDependencyGraphRole.Deployment,
                PowerShellCompilationDependencyGraphDisposition.Compiled,
                "Explicit source-graph build input.",
                targetFramework,
                runtimeIdentifier);
            if (inputId != rootId)
                AddEdge(rootId, inputId, PowerShellCompilationDependencyEdgeKind.BuildInput, Relative(input));
            DiscoverSourceEdges(input, inputId, targetFramework, runtimeIdentifier);
        }

        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            DiscoverManifestEdges(Path.GetFullPath(manifestPath!), rootId, targetFramework, runtimeIdentifier, new HashSet<string>(PowerShellCompilationPathSafety.PathComparer));

        DiscoverManagedDependencyClosure(targetFramework, runtimeIdentifier);
        AddCompilerPackageNodes(rootId, targetFramework);
        if (includeRuntimePack) AddRuntimePackNodes(rootId, targetFramework, runtimeIdentifier);

        var nodes = _nodes.Values.OrderBy(static node => node.Id, StringComparer.Ordinal).ToArray();
        var edges = _edges
            .GroupBy(static edge => edge.FromId + "\0" + edge.ToId + "\0" + edge.Kind, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(static edge => edge.Order).First())
            .OrderBy(static edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Order)
            .ThenBy(static edge => edge.ToId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < edges.Length; index++) edges[index].Order = index;
        var cycles = FindCycles(nodes, edges);
        var conflicts = FindConflicts(nodes);
        return new PowerShellCompilationDependencyGraph
        {
            RootNodeId = rootId,
            Nodes = nodes,
            Edges = edges,
            Cycles = cycles,
            Conflicts = conflicts,
            LockSha256 = PowerShellCompilationDependencyLockHasher.ComputeSha256(new PowerShellCompilationDependencyGraph
            {
                RootNodeId = rootId,
                Nodes = nodes,
                Edges = edges,
                Cycles = cycles,
                Conflicts = conflicts
            })
        };
    }

    private void AddCompilerPackageNodes(string rootId, string? targetFramework)
    {
        foreach (var package in PowerShellCompilationGeneratedPackageCatalog.Select(_artifactKind, _mode, targetFramework))
        {
            var id = StableId("compiler-package", package.Id.ToUpperInvariant(), package.Version, package.ContentHash);
            _nodes.Add(id, new PowerShellCompilationDependencyNode
            {
                Id = id,
                Kind = PowerShellCompilationDependencyNodeKind.NuGetPackage,
                Roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Build,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = package.Id,
                    Version = package.Version,
                    ContentHashAlgorithm = "SHA-512",
                    ContentHash = package.ContentHash,
                    Source = "https://api.nuget.org/v3/index.json",
                    TargetFramework = targetFramework ?? string.Empty,
                    Provenance = "EmbeddedCompilerPackageCatalog"
                },
                Disposition = PowerShellCompilationDependencyGraphDisposition.Referenced,
                Exists = false,
                Note = "Immutable compiler-owned generated-project package reference.",
                Policy = new PowerShellCompilationDependencyPolicy
                {
                    Redistribution = "PackageLicense",
                    Servicing = "PowerForgeCompilerCatalog"
                }
            });
            AddEdge(rootId, id, PowerShellCompilationDependencyEdgeKind.CompilerPackage, package.Id + "/" + package.Version);
        }
    }

    private string AddDependencyNode(
        PowerShellCompilationDependency dependency,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        if (dependency.SourcePath is not null)
        {
            return AddLocalNode(
                dependency.SourcePath,
                ClassifyDependency(dependency),
                GetRoles(dependency),
                MapDisposition(dependency),
                dependency.Note,
                targetFramework,
                runtimeIdentifier);
        }

        var kind = dependency.Kind == PowerShellCompilationDependencyKind.RequiredModule
            ? PowerShellCompilationDependencyNodeKind.ExternalModule
            : dependency.Kind == PowerShellCompilationDependencyKind.ManagedAssembly
                ? PowerShellCompilationDependencyNodeKind.ManagedLibrary
                : PowerShellCompilationDependencyNodeKind.Content;
        return AddExternalNode(
            dependency.Name,
            kind,
            PowerShellCompilationDependencyGraphDisposition.External,
            dependency.Note,
            version: string.Empty,
            targetFramework,
            runtimeIdentifier);
    }

    private string AddLocalNode(
        string path,
        PowerShellCompilationDependencyNodeKind kind,
        PowerShellCompilationDependencyGraphRole roles,
        PowerShellCompilationDependencyGraphDisposition disposition,
        string note,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        path = Path.GetFullPath(path);
        if (_pathNodes.TryGetValue(path, out var existingId))
        {
            var existing = _nodes[existingId];
            existing.Roles |= roles;
            existing.Disposition = MergeDisposition(existing.Disposition, disposition);
            existing.Policy.Redistribution = existing.Disposition == PowerShellCompilationDependencyGraphDisposition.Bundled
                ? "Unverified"
                : "NotApplicable";
            existing.Policy.Servicing = existing.Disposition == PowerShellCompilationDependencyGraphDisposition.External
                ? "TargetEnvironment"
                : "ArtifactOwner";
            return existingId;
        }

        var exists = File.Exists(path);
        var sha = exists ? ComputeFileHash(path) : string.Empty;
        var relative = Relative(path);
        var id = StableId("local", kind.ToString(), relative.ToUpperInvariant(), sha);
        var identity = new PowerShellCompilationDependencyIdentity
        {
            Name = Path.GetFileName(path),
            Sha256 = sha,
            Source = relative,
            TargetFramework = targetFramework ?? string.Empty,
            RuntimeIdentifier = runtimeIdentifier ?? string.Empty,
            Provenance = exists ? "LocalReadOnlyResolution" : "MissingLocalInput"
        };
        if (exists && kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule)
            ReadManagedIdentity(path, identity);
        else if (exists && kind == PowerShellCompilationDependencyNodeKind.ModuleManifest)
            identity.Version = ModuleManifestValueReader.ReadTopLevelString(path, "ModuleVersion") ?? string.Empty;
        else if (exists && kind == PowerShellCompilationDependencyNodeKind.NativeLibrary)
            identity.Architecture = ReadPortableExecutableArchitecture(path);

        _nodes.Add(id, new PowerShellCompilationDependencyNode
        {
            Id = id,
            Kind = kind,
            Roles = roles,
            Identity = identity,
            Disposition = disposition,
            Exists = exists,
            Note = note,
            Policy = new PowerShellCompilationDependencyPolicy
            {
                Redistribution = disposition == PowerShellCompilationDependencyGraphDisposition.Bundled ? "Unverified" : "NotApplicable",
                Servicing = disposition == PowerShellCompilationDependencyGraphDisposition.External ? "TargetEnvironment" : "ArtifactOwner"
            },
            Interop = kind == PowerShellCompilationDependencyNodeKind.NativeLibrary
                ? new PowerShellCompilationInteropBoundaryContract
                {
                    Owner = disposition == PowerShellCompilationDependencyGraphDisposition.Rejected ? "TypedNativeAdapterRequired" : "PowerShellHostedNative",
                    Platform = runtimeIdentifier ?? "TargetEnvironment",
                    Errors = disposition == PowerShellCompilationDependencyGraphDisposition.Rejected ? "RejectedBeforePublication" : "NativeErrorToPowerShellError",
                    Cancellation = disposition == PowerShellCompilationDependencyGraphDisposition.Rejected ? "ExplicitAdapterRequired" : "HostStop",
                    Cleanup = disposition == PowerShellCompilationDependencyGraphDisposition.Rejected ? "ExplicitHandleAndUnloadRequired" : "PowerShellHostLifetime",
                    Threading = "AdapterDeclared"
                }
                : new PowerShellCompilationInteropBoundaryContract()
        });
        _pathNodes.Add(path, id);
        return id;
    }

    private string AddExternalNode(
        string name,
        PowerShellCompilationDependencyNodeKind kind,
        PowerShellCompilationDependencyGraphDisposition disposition,
        string note,
        string version,
        string? targetFramework,
        string? runtimeIdentifier,
        string? identityDiscriminator = null)
    {
        var normalizedName = name.Trim();
        var id = StableId(
            "external",
            kind.ToString(),
            normalizedName.ToUpperInvariant(),
            version.ToUpperInvariant(),
            (identityDiscriminator ?? string.Empty).ToUpperInvariant());
        if (_nodes.TryGetValue(id, out var existing))
        {
            existing.Roles |= PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment;
            return id;
        }
        _nodes.Add(id, new PowerShellCompilationDependencyNode
        {
            Id = id,
            Kind = kind,
            Roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
            Identity = new PowerShellCompilationDependencyIdentity
            {
                Name = normalizedName,
                Version = version,
                Source = "External",
                TargetFramework = targetFramework ?? string.Empty,
                RuntimeIdentifier = runtimeIdentifier ?? string.Empty,
                Provenance = "DeclaredExternalRequirement"
            },
            Disposition = disposition,
            Exists = false,
            Note = note,
            Policy = new PowerShellCompilationDependencyPolicy
            {
                Redistribution = "NotBundled",
                Servicing = "TargetEnvironment"
            }
        });
        return id;
    }

    private void AddEdge(string fromId, string toId, PowerShellCompilationDependencyEdgeKind kind, string evidence)
    {
        if (fromId == toId && kind is PowerShellCompilationDependencyEdgeKind.BuildInput or
            PowerShellCompilationDependencyEdgeKind.RuntimeAsset or
            PowerShellCompilationDependencyEdgeKind.Metadata)
            return;
        var order = _edges.Count;
        _edges.Add(new PowerShellCompilationDependencyEdge
        {
            Id = StableId("edge", fromId, toId, kind.ToString()),
            FromId = fromId,
            ToId = toId,
            Kind = kind,
            Order = order,
            Evidence = evidence
        });
    }

    private string Relative(string path)
    {
        path = Path.GetFullPath(path);
        var rootPrefix = _moduleRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return PowerShellCompilationPathSafety.PathEquals(_moduleRoot, path) || PowerShellCompilationPathSafety.PathStartsWith(path, rootPrefix)
            ? FrameworkCompatibility.GetRelativePath(_moduleRoot, path).Replace('\\', '/')
            : path.Replace('\\', '/');
    }

    private static PowerShellCompilationDependencyGraphRole GetRoles(PowerShellCompilationDependency dependency)
    {
        var roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment;
        if (dependency.Kind is PowerShellCompilationDependencyKind.PowerShellSource or PowerShellCompilationDependencyKind.ModuleManifest)
            roles |= PowerShellCompilationDependencyGraphRole.Semantic;
        return roles;
    }

    private PowerShellCompilationDependencyGraphDisposition MapDisposition(PowerShellCompilationDependency dependency)
        => dependency.Disposition switch
        {
            PowerShellCompilationDependencyDisposition.Compiled => PowerShellCompilationDependencyGraphDisposition.Compiled,
            PowerShellCompilationDependencyDisposition.PreservedScript => PowerShellCompilationDependencyGraphDisposition.Hosted,
            PowerShellCompilationDependencyDisposition.Embedded or
            PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted or
            PowerShellCompilationDependencyDisposition.CopiedAdjacent => PowerShellCompilationDependencyGraphDisposition.Bundled,
            PowerShellCompilationDependencyDisposition.ExternalRequirement => PowerShellCompilationDependencyGraphDisposition.External,
            PowerShellCompilationDependencyDisposition.Missing => PowerShellCompilationDependencyGraphDisposition.Rejected,
            _ => _mode == PowerShellCompilationMode.Strict
                ? PowerShellCompilationDependencyGraphDisposition.Rejected
                : PowerShellCompilationDependencyGraphDisposition.Hosted
        };

    private static PowerShellCompilationDependencyGraphDisposition MergeDisposition(
        PowerShellCompilationDependencyGraphDisposition left,
        PowerShellCompilationDependencyGraphDisposition right)
    {
        if (left == right) return left;
        if (left == PowerShellCompilationDependencyGraphDisposition.Rejected || right == PowerShellCompilationDependencyGraphDisposition.Rejected)
            return PowerShellCompilationDependencyGraphDisposition.Rejected;
        if (left == PowerShellCompilationDependencyGraphDisposition.Compiled || right == PowerShellCompilationDependencyGraphDisposition.Compiled)
            return PowerShellCompilationDependencyGraphDisposition.Compiled;
        if (left == PowerShellCompilationDependencyGraphDisposition.Bundled || right == PowerShellCompilationDependencyGraphDisposition.Bundled)
            return PowerShellCompilationDependencyGraphDisposition.Bundled;
        return right;
    }

    private static PowerShellCompilationDependencyNodeKind ClassifyDependency(PowerShellCompilationDependency dependency)
        => dependency.Kind switch
        {
            PowerShellCompilationDependencyKind.ModuleManifest => PowerShellCompilationDependencyNodeKind.ModuleManifest,
            PowerShellCompilationDependencyKind.ManagedAssembly when dependency.Discovery == PowerShellCompilationDependencyDiscovery.NestedModules => PowerShellCompilationDependencyNodeKind.BinaryModule,
            PowerShellCompilationDependencyKind.ManagedAssembly => PowerShellCompilationDependencyNodeKind.ManagedLibrary,
            PowerShellCompilationDependencyKind.NativeLibrary => PowerShellCompilationDependencyNodeKind.NativeLibrary,
            PowerShellCompilationDependencyKind.TypeData => PowerShellCompilationDependencyNodeKind.TypeData,
            PowerShellCompilationDependencyKind.FormatData => PowerShellCompilationDependencyNodeKind.FormatData,
            PowerShellCompilationDependencyKind.PowerShellSource => ClassifyLocalPath(dependency.SourcePath ?? dependency.Name),
            _ => PowerShellCompilationDependencyNodeKind.Content
        };

    private static PowerShellCompilationDependencyNodeKind ClassifyLocalPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyNodeKind.Script;
        if (extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                if (text.IndexOf("Import-PSSession", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("Export-PSSession", StringComparison.OrdinalIgnoreCase) >= 0)
                    return PowerShellCompilationDependencyNodeKind.DynamicProxyModule;
            }
            return PowerShellCompilationDependencyNodeKind.ScriptModule;
        }
        if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyNodeKind.ModuleManifest;
        if (extension.Equals(".cdxml", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyNodeKind.CdxmlModule;
        if (extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(path).Contains("format", StringComparison.OrdinalIgnoreCase)
                ? PowerShellCompilationDependencyNodeKind.FormatData
                : PowerShellCompilationDependencyNodeKind.TypeData;
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? PowerShellCompilationDependencyNodeKind.ExternalProcess
                    : PowerShellCompilationDependencyNodeKind.ManagedLibrary;
            try
            {
                _ = AssemblyName.GetAssemblyName(path);
                return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? PowerShellCompilationDependencyNodeKind.ManagedLibrary
                    : PowerShellCompilationDependencyNodeKind.ManagedLibrary;
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException or DirectoryNotFoundException)
            {
                return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? PowerShellCompilationDependencyNodeKind.ExternalProcess
                    : PowerShellCompilationDependencyNodeKind.NativeLibrary;
            }
        }
        return PowerShellCompilationDependencyNodeKind.Content;
    }

    private static PowerShellCompilationDependencyEdgeKind MapEdgeKind(PowerShellCompilationDependencyDiscovery discovery)
        => discovery switch
        {
            PowerShellCompilationDependencyDiscovery.RootModule => PowerShellCompilationDependencyEdgeKind.RootModule,
            PowerShellCompilationDependencyDiscovery.RequiredModules => PowerShellCompilationDependencyEdgeKind.RequiredModule,
            PowerShellCompilationDependencyDiscovery.NestedModules => PowerShellCompilationDependencyEdgeKind.NestedModule,
            PowerShellCompilationDependencyDiscovery.RequiredAssemblies => PowerShellCompilationDependencyEdgeKind.RequiredAssembly,
            PowerShellCompilationDependencyDiscovery.ScriptsToProcess => PowerShellCompilationDependencyEdgeKind.ModuleInitialization,
            PowerShellCompilationDependencyDiscovery.TypesToProcess or PowerShellCompilationDependencyDiscovery.FormatsToProcess => PowerShellCompilationDependencyEdgeKind.Metadata,
            PowerShellCompilationDependencyDiscovery.SourceGraph => PowerShellCompilationDependencyEdgeKind.BuildInput,
            _ => PowerShellCompilationDependencyEdgeKind.RuntimeAsset
        };

    private static void ReadManagedIdentity(string path, PowerShellCompilationDependencyIdentity identity)
    {
        try
        {
            var assembly = AssemblyName.GetAssemblyName(path);
            identity.Name = assembly.Name ?? identity.Name;
            identity.Version = assembly.Version?.ToString() ?? string.Empty;
            identity.PublicKeyToken = string.Concat((assembly.GetPublicKeyToken() ?? Array.Empty<byte>())
                .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            identity.Culture = PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(assembly.CultureName);
            identity.Retargetable = assembly.Flags.HasFlag(AssemblyNameFlags.Retargetable);
            identity.ContentType = PowerShellTargetRuntimeAssemblyCatalog.NormalizeContentType(assembly.ContentType.ToString());
            identity.Architecture = ReadPortableExecutableArchitecture(path);
            identity.Provenance = "ManagedMetadataReadOnly";
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            identity.Provenance = "UnrecognizedBinaryMetadata";
        }
    }

    private static string ReadPortableExecutableArchitecture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.CoffHeader.Machine.ToString();
        }
        catch (BadImageFormatException)
        {
            return "Unknown";
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    private static string StableId(params string[] parts)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\0", parts))));
    }

    private static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
}
