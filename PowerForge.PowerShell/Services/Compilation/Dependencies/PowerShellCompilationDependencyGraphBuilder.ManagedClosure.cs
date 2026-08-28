using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
    private void DiscoverManagedDependencyClosure(string? targetFramework, string? runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(targetFramework)) return;
        var targetRuntime = PowerShellTargetRuntimeAssemblyCatalog.ReadStableKeys(targetFramework!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(_pathNodes
            .Where(pair => IsManagedNode(_nodes[pair.Value], pair.Key))
            .Select(static pair => pair.Key)
            .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer));
        var inspected = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        while (pending.Count > 0)
        {
            var path = pending.Dequeue();
            if (!inspected.Add(path) || !File.Exists(path) || !_pathNodes.TryGetValue(path, out var parentId)) continue;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) continue;
            var directory = Path.GetDirectoryName(path) ?? _moduleRoot;

            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                var name = reader.GetString(reference.Name);
                var tokenBytes = reader.GetBlobBytes(reference.PublicKeyOrToken);
                var token = (reference.Flags & AssemblyFlags.PublicKey) != 0 && tokenBytes.Length > 0
                    ? PowerShellTargetRuntimeAssemblyCatalog.ComputePublicKeyToken(tokenBytes)
                    : string.Concat(tokenBytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
                var culture = reference.Culture.IsNil ? string.Empty : reader.GetString(reference.Culture);
                var stableKey = PowerShellTargetRuntimeAssemblyCatalog.CreateStableKey(name, reference.Version, token, culture);
                if (targetRuntime.Contains(stableKey)) continue;

                var adjacent = FindAdjacentManagedAssembly(directory, name, reference.Version, token, culture);
                string nodeId;
                if (adjacent is null)
                {
                    nodeId = AddExternalManagedNode(name, reference.Version, token, culture, targetFramework!, runtimeIdentifier);
                }
                else
                {
                    nodeId = AddLocalNode(
                        adjacent,
                        PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                        PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                        PowerShellCompilationDependencyGraphDisposition.Referenced,
                        "Exact transitive managed assembly reference discovered from CLR metadata without loading the assembly.",
                        targetFramework,
                        runtimeIdentifier);
                    pending.Enqueue(adjacent);
                }
                AddEdge(parentId, nodeId, PowerShellCompilationDependencyEdgeKind.ManagedReference, stableKey);
            }

            foreach (var import in ReadNativeImports(reader))
            {
                var adjacent = FindAdjacentNativeLibrary(directory, import);
                var disposition = adjacent is not null
                    ? PowerShellCompilationDependencyGraphDisposition.Referenced
                    : string.IsNullOrWhiteSpace(runtimeIdentifier)
                        ? PowerShellCompilationDependencyGraphDisposition.Rejected
                        : PowerShellCompilationDependencyGraphDisposition.External;
                var nodeId = adjacent is not null
                    ? AddLocalNode(
                        adjacent,
                        PowerShellCompilationDependencyNodeKind.NativeLibrary,
                        PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                        disposition,
                        "Native library imported by a managed dependency and resolved adjacent without loading it.",
                        targetFramework,
                        runtimeIdentifier)
                    : AddExternalNode(
                        import,
                        PowerShellCompilationDependencyNodeKind.NativeLibrary,
                        disposition,
                        "Native library imported by a managed dependency; target availability is an explicit RID requirement.",
                        string.Empty,
                        targetFramework,
                        runtimeIdentifier);
                _nodes[nodeId].Interop = new PowerShellCompilationInteropBoundaryContract
                {
                    Owner = "ManagedPInvoke",
                    Platform = runtimeIdentifier ?? "UnspecifiedTarget",
                    Errors = "NativeLoaderError",
                    Cancellation = "ManagedCallerContract",
                    Cleanup = "ManagedWrapperContract",
                    Threading = "ManagedWrapperContract"
                };
                AddEdge(parentId, nodeId, PowerShellCompilationDependencyEdgeKind.NativeLoad, import);
            }
        }
    }

    private string AddExternalManagedNode(
        string name,
        Version version,
        string publicKeyToken,
        string culture,
        string targetFramework,
        string? runtimeIdentifier)
    {
        var id = StableId(
            "external-managed",
            name.ToUpperInvariant(),
            version.ToString(),
            publicKeyToken.ToUpperInvariant(),
            PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(culture).ToUpperInvariant());
        if (_nodes.ContainsKey(id)) return id;
        _nodes.Add(id, new PowerShellCompilationDependencyNode
        {
            Id = id,
            Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
            Roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
            Exists = false,
            Disposition = PowerShellCompilationDependencyGraphDisposition.External,
            Note = "Exact transitive managed assembly reference is not adjacent and remains an explicit restore requirement.",
            Identity = new PowerShellCompilationDependencyIdentity
            {
                Name = name,
                Version = version.ToString(),
                PublicKeyToken = publicKeyToken,
                Culture = PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(culture),
                Source = "External",
                TargetFramework = targetFramework,
                RuntimeIdentifier = runtimeIdentifier ?? string.Empty,
                Provenance = "ManagedAssemblyReferenceMetadata"
            },
            Policy = new PowerShellCompilationDependencyPolicy
            {
                Redistribution = "NotBundled",
                Servicing = "TargetEnvironment"
            }
        });
        return id;
    }

    private static string? FindAdjacentManagedAssembly(string directory, string name, Version version, string publicKeyToken, string culture)
    {
        foreach (var extension in new[] { ".dll", ".exe" })
        {
            var candidate = Path.Combine(directory, name + extension);
            if (!File.Exists(candidate)) continue;
            try
            {
                var assembly = AssemblyName.GetAssemblyName(candidate);
                var token = string.Concat((assembly.GetPublicKeyToken() ?? Array.Empty<byte>())
                    .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
                var assemblyCulture = PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(assembly.CultureName);
                if (assembly.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true &&
                    assembly.Version == version && token.Equals(publicKeyToken, StringComparison.OrdinalIgnoreCase) &&
                    assemblyCulture.Equals(PowerShellTargetRuntimeAssemblyCatalog.NormalizeCulture(culture), StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(candidate);
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                // A native or unreadable adjacent file cannot satisfy an exact managed identity.
            }
        }
        return null;
    }

    private static string[] ReadNativeImports(MetadataReader reader)
    {
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
            var import = method.GetImport();
            if (import.Module.IsNil) continue;
            var name = reader.GetString(reader.GetModuleReference(import.Module).Name);
            if (!string.IsNullOrWhiteSpace(name)) imports.Add(name);
        }
        return imports.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? FindAdjacentNativeLibrary(string directory, string import)
    {
        var names = new[]
        {
            import,
            import + ".dll",
            "lib" + import + ".so",
            "lib" + import + ".dylib"
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        return names.Select(name => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static bool IsManagedNode(PowerShellCompilationDependencyNode node, string path)
    {
        if (!node.Exists || node.Kind is not (PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule))
            return false;
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
