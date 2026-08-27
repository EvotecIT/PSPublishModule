using System.Text;
using System.Security.Cryptography;

namespace PowerForge;

internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
    private void DiscoverManifestEdges(
        string manifestPath,
        string parentId,
        string? targetFramework,
        string? runtimeIdentifier,
        ISet<string> visited)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var manifestId = AddLocalNode(
            manifestPath,
            PowerShellCompilationDependencyNodeKind.ModuleManifest,
            PowerShellCompilationDependencyGraphRole.Semantic |
            PowerShellCompilationDependencyGraphRole.Dependency |
            PowerShellCompilationDependencyGraphRole.Deployment,
            _artifactKind == PowerShellCompilationArtifactKind.BinaryModule
                ? PowerShellCompilationDependencyGraphDisposition.Bundled
                : PowerShellCompilationDependencyGraphDisposition.Referenced,
            "Module manifest metadata read without importing the module.",
            targetFramework,
            runtimeIdentifier);
        if (manifestId != parentId)
            AddEdge(parentId, manifestId, PowerShellCompilationDependencyEdgeKind.BuildInput, Relative(manifestPath));
        if (!visited.Add(manifestPath)) return;
        var directory = Path.GetDirectoryName(manifestPath) ?? _moduleRoot;

        var rootModule = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(manifestPath, "RootModule");
        var nestedModuleValues = ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules").ToArray();
        _nodes[manifestId].Kind = ClassifyManifestModule(manifestPath, rootModule, nestedModuleValues);
        if (!string.IsNullOrWhiteSpace(rootModule))
            AddManifestTarget(manifestId, directory, rootModule!, PowerShellCompilationDependencyEdgeKind.RootModule, targetFramework, runtimeIdentifier, visited);

        if (ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? modules) && modules is not null)
        {
            foreach (var module in modules.OrderBy(static item => item.ModuleName, StringComparer.OrdinalIgnoreCase))
            {
                var version = module.RequiredVersion ?? module.ModuleVersion ??
                    (string.IsNullOrWhiteSpace(module.MaximumVersion) ? string.Empty : "<=" + module.MaximumVersion);
                var nodeId = AddExternalNode(
                    module.ModuleName,
                    PowerShellCompilationDependencyNodeKind.ExternalModule,
                    HostedOrRejected(),
                    "RequiredModules identity is locked but acquisition remains an explicit restore operation.",
                    version ?? string.Empty,
                    targetFramework,
                    runtimeIdentifier);
                if (!string.IsNullOrWhiteSpace(module.Guid))
                    _nodes[nodeId].Identity.Provenance = "ManifestRequiredModule;Guid=" + module.Guid;
                AddEdge(manifestId, nodeId, PowerShellCompilationDependencyEdgeKind.RequiredModule, module.ModuleName);
            }
        }

        AddManifestArray("NestedModules", PowerShellCompilationDependencyEdgeKind.NestedModule);
        AddManifestArray("RequiredAssemblies", PowerShellCompilationDependencyEdgeKind.RequiredAssembly);
        AddManifestArray("ScriptsToProcess", PowerShellCompilationDependencyEdgeKind.ModuleInitialization);
        AddManifestArray("TypesToProcess", PowerShellCompilationDependencyEdgeKind.Metadata);
        AddManifestArray("FormatsToProcess", PowerShellCompilationDependencyEdgeKind.Metadata);
        AddManifestArray("FileList", PowerShellCompilationDependencyEdgeKind.RuntimeAsset);

        void AddManifestArray(string key, PowerShellCompilationDependencyEdgeKind edgeKind)
        {
            IEnumerable<string> values = key == "NestedModules"
                ? nestedModuleValues
                : ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath, key) ?? Array.Empty<string>();
            foreach (var value in values)
                AddManifestTarget(manifestId, directory, value, edgeKind, targetFramework, runtimeIdentifier, visited);
        }
    }

    private static PowerShellCompilationDependencyNodeKind ClassifyManifestModule(
        string manifestPath,
        string? rootModule,
        IReadOnlyCollection<string> nestedModules)
    {
        var rootExtension = Path.GetExtension(rootModule ?? string.Empty);
        if (rootExtension.Equals(".cdxml", StringComparison.OrdinalIgnoreCase))
            return PowerShellCompilationDependencyNodeKind.CdxmlModule;
        if (rootExtension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return PowerShellCompilationDependencyNodeKind.BinaryModule;
        if (rootExtension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            var rootPath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, (rootModule ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(rootPath))
            {
                var text = File.ReadAllText(rootPath);
                if (text.IndexOf("Import-PSSession", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("Export-PSSession", StringComparison.OrdinalIgnoreCase) >= 0)
                    return PowerShellCompilationDependencyNodeKind.DynamicProxyModule;
            }
            if (nestedModules.Any(static nested => Path.GetExtension(nested).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
                return PowerShellCompilationDependencyNodeKind.MixedModule;
            return PowerShellCompilationDependencyNodeKind.ScriptModule;
        }
        return PowerShellCompilationDependencyNodeKind.ModuleManifest;
    }

    private void AddManifestTarget(
        string manifestId,
        string directory,
        string value,
        PowerShellCompilationDependencyEdgeKind edgeKind,
        string? targetFramework,
        string? runtimeIdentifier,
        ISet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var kind = edgeKind switch
        {
            PowerShellCompilationDependencyEdgeKind.RequiredAssembly => PowerShellCompilationDependencyNodeKind.ManagedLibrary,
            PowerShellCompilationDependencyEdgeKind.Metadata when value.EndsWith(".format.ps1xml", StringComparison.OrdinalIgnoreCase) => PowerShellCompilationDependencyNodeKind.FormatData,
            PowerShellCompilationDependencyEdgeKind.Metadata => PowerShellCompilationDependencyNodeKind.TypeData,
            _ => ClassifyLocalPath(value)
        };
        var disposition = edgeKind switch
        {
            PowerShellCompilationDependencyEdgeKind.ModuleInitialization when _mode == PowerShellCompilationMode.Strict => PowerShellCompilationDependencyGraphDisposition.Rejected,
            PowerShellCompilationDependencyEdgeKind.RequiredAssembly => PowerShellCompilationDependencyGraphDisposition.Referenced,
            PowerShellCompilationDependencyEdgeKind.RootModule when kind is PowerShellCompilationDependencyNodeKind.ScriptModule or PowerShellCompilationDependencyNodeKind.Script =>
                _mode == PowerShellCompilationMode.Strict ? PowerShellCompilationDependencyGraphDisposition.Compiled : PowerShellCompilationDependencyGraphDisposition.Hosted,
            _ => _artifactKind == PowerShellCompilationArtifactKind.BinaryModule
                ? PowerShellCompilationDependencyGraphDisposition.Bundled
                : PowerShellCompilationDependencyGraphDisposition.Referenced
        };
        var nodeId = AddReference(
            value,
            directory,
            kind,
            disposition,
            "Static module manifest " + edgeKind + " reference.",
            targetFramework,
            runtimeIdentifier);
        AddEdge(manifestId, nodeId, edgeKind, value);
        if (_nodes[nodeId].Exists && _nodes[nodeId].Kind == PowerShellCompilationDependencyNodeKind.ModuleManifest)
            DiscoverManifestEdges(_nodes[nodeId].Identity.Source.StartsWith("External", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : Path.Combine(_moduleRoot, _nodes[nodeId].Identity.Source.Replace('/', Path.DirectorySeparatorChar)),
                manifestId,
                targetFramework,
                runtimeIdentifier,
                visited);
    }

    private static string[][] FindCycles(
        IReadOnlyCollection<PowerShellCompilationDependencyNode> nodes,
        IReadOnlyCollection<PowerShellCompilationDependencyEdge> edges)
    {
        var adjacency = edges.GroupBy(static edge => edge.FromId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.ToId).Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cycles = new List<string[]>();
        foreach (var node in nodes.OrderBy(static item => item.Id, StringComparer.Ordinal))
            Visit(node.Id);
        return cycles
            .GroupBy(static cycle => string.Join("\0", cycle), StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static cycle => string.Join("\0", cycle), StringComparer.Ordinal)
            .ToArray();

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
            {
                var start = stack.IndexOf(id);
                if (start >= 0) cycles.Add(stack.Skip(start).Append(id).ToArray());
                return;
            }
            stack.Add(id);
            if (adjacency.TryGetValue(id, out var targets))
            {
                foreach (var target in targets) Visit(target);
            }
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static string[] FindConflicts(IEnumerable<PowerShellCompilationDependencyNode> nodes)
        => nodes
            .Where(static node => node.Kind is PowerShellCompilationDependencyNodeKind.ExternalModule or
                PowerShellCompilationDependencyNodeKind.ManagedLibrary or
                PowerShellCompilationDependencyNodeKind.BinaryModule)
            .GroupBy(static node => node.Identity.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(static node => node.Identity.Version).Where(static version => version.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => $"Dependency '{group.Key}' has incompatible locked versions: {string.Join(", ", group.Select(static node => node.Identity.Version).Where(static version => version.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static version => version, StringComparer.OrdinalIgnoreCase))}.")
            .OrderBy(static conflict => conflict, StringComparer.Ordinal)
            .ToArray();

    private static string ComputeLockHash(
        IEnumerable<PowerShellCompilationDependencyNode> nodes,
        IEnumerable<PowerShellCompilationDependencyEdge> edges,
        IEnumerable<string[]> cycles,
        IEnumerable<string> conflicts)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes.OrderBy(static item => item.Id, StringComparer.Ordinal))
        {
            Append("node", node.Id, node.Kind, node.Roles, node.Disposition, node.Exists,
                node.Identity.Name, node.Identity.Version, node.Identity.Sha256, node.Identity.Source,
                node.Identity.Edition, node.Identity.TargetFramework, node.Identity.RuntimeIdentifier,
                node.Identity.Architecture, node.Identity.Provenance, node.Policy.Redistribution,
                node.Policy.Publisher, node.Policy.Signature, node.Policy.Servicing, node.Policy.License);
        }
        foreach (var edge in edges.OrderBy(static item => item.FromId, StringComparer.Ordinal).ThenBy(static item => item.Order))
            Append("edge", edge.FromId, edge.ToId, edge.Kind, edge.Evidence);
        foreach (var cycle in cycles) Append("cycle", string.Join("->", cycle));
        foreach (var conflict in conflicts) Append("conflict", conflict);
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));

        void Append(params object[] values)
        {
            foreach (var value in values)
            {
                var text = value?.ToString() ?? string.Empty;
                builder.Append(text.Length).Append(':').Append(text);
            }
            builder.AppendLine();
        }
    }
}
