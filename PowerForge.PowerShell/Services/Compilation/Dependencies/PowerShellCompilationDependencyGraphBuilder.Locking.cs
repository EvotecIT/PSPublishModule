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
                var localManifest = TryResolveRequiredModuleManifest(directory, module);
                if (localManifest is not null)
                    ValidateResolvedModuleIdentity(localManifest, module);
                var nodeId = localManifest is null
                    ? AddExternalNode(
                        module.ModuleName,
                        PowerShellCompilationDependencyNodeKind.ExternalModule,
                        HostedOrRejected(),
                        "RequiredModules identity is locked; unresolved acquisition remains an explicit restore operation.",
                        module.RequiredVersion ?? module.ModuleVersion ?? module.MaximumVersion ?? string.Empty,
                        targetFramework,
                        runtimeIdentifier)
                    : AddLocalNode(
                        localManifest,
                        PowerShellCompilationDependencyNodeKind.ModuleManifest,
                        PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                        HostedOrRejected(),
                        "RequiredModules resolved transitively from a local read-only manifest.",
                        targetFramework,
                        runtimeIdentifier);
                ApplyModuleIdentity(_nodes[nodeId].Identity, module);
                AddEdge(manifestId, nodeId, PowerShellCompilationDependencyEdgeKind.RequiredModule, module.ModuleName);
                if (localManifest is not null)
                    DiscoverManifestEdges(localManifest, nodeId, targetFramework, runtimeIdentifier, visited);
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

    private string? TryResolveRequiredModuleManifest(string directory, RequiredModuleReference module)
    {
        var name = module.ModuleName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(name);
        var candidates = new List<string>();
        if (name.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            candidates.Add(Path.Combine(directory, name));
        candidates.Add(Path.Combine(directory, fileName + ".psd1"));
        candidates.Add(Path.Combine(directory, fileName, fileName + ".psd1"));
        candidates.Add(Path.Combine(_moduleRoot, fileName, fileName + ".psd1"));
        foreach (var versionRoot in new[] { Path.Combine(directory, fileName), Path.Combine(_moduleRoot, fileName) }
                     .Select(Path.GetFullPath)
                     .Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            if (!Directory.Exists(versionRoot)) continue;
            foreach (var versionDirectory in Directory.EnumerateDirectories(versionRoot, "*", SearchOption.TopDirectoryOnly))
                candidates.Add(Path.Combine(versionDirectory, fileName + ".psd1"));
        }

        return candidates.Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .Where(File.Exists)
            .Select(path => new { Path = path, Version = GetMatchingModuleVersion(path, module) })
            .Where(static candidate => candidate.Version is not null)
            .OrderByDescending(static candidate => candidate.Version)
            .ThenBy(static candidate => candidate.Path, PowerShellCompilationPathSafety.PathComparer)
            .Select(static candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static Version? GetMatchingModuleVersion(string manifestPath, RequiredModuleReference module)
    {
        var actualText = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (!Version.TryParse(actualText, out var actual)) return null;
        if (Version.TryParse(module.RequiredVersion, out var required) && actual != required) return null;
        if (Version.TryParse(module.ModuleVersion, out var minimum) && actual < minimum) return null;
        if (Version.TryParse(module.MaximumVersion, out var maximum) && actual > maximum) return null;
        if (Guid.TryParse(module.Guid, out var requiredGuid))
        {
            var actualGuid = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "GUID");
            if (!Guid.TryParse(actualGuid, out var parsedGuid) || parsedGuid != requiredGuid) return null;
        }
        return actual;
    }

    private static void ApplyModuleIdentity(
        PowerShellCompilationDependencyIdentity identity,
        RequiredModuleReference module)
    {
        identity.Name = module.ModuleName;
        identity.Version = module.RequiredVersion ?? module.ModuleVersion ?? module.MaximumVersion ?? string.Empty;
        identity.MinimumVersion = module.ModuleVersion ?? string.Empty;
        identity.RequiredVersion = module.RequiredVersion ?? string.Empty;
        identity.MaximumVersion = module.MaximumVersion ?? string.Empty;
        identity.Guid = module.Guid ?? string.Empty;
        identity.Provenance = "ManifestRequiredModule";
    }

    private static void ValidateResolvedModuleIdentity(string manifestPath, RequiredModuleReference module)
    {
        var actualText = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion") ?? string.Empty;
        if (!Version.TryParse(actualText, out var actual))
            throw new InvalidOperationException($"Resolved RequiredModules manifest '{manifestPath}' has no valid literal ModuleVersion.");
        if (Version.TryParse(module.RequiredVersion, out var required) && actual != required)
            throw new InvalidOperationException($"Resolved RequiredModules manifest '{manifestPath}' version {actual} does not match required version {required}.");
        if (Version.TryParse(module.ModuleVersion, out var minimum) && actual < minimum)
            throw new InvalidOperationException($"Resolved RequiredModules manifest '{manifestPath}' version {actual} is below minimum version {minimum}.");
        if (Version.TryParse(module.MaximumVersion, out var maximum) && actual > maximum)
            throw new InvalidOperationException($"Resolved RequiredModules manifest '{manifestPath}' version {actual} exceeds maximum version {maximum}.");
        if (!Guid.TryParse(module.Guid, out var requiredGuid)) return;
        var actualGuid = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "GUID");
        if (!Guid.TryParse(actualGuid, out var parsedGuid) || parsedGuid != requiredGuid)
            throw new InvalidOperationException($"Resolved RequiredModules manifest '{manifestPath}' GUID does not match required GUID {requiredGuid:D}.");
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

}
