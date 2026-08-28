namespace PowerForge;

internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
    private void AddRuntimePackNodes(string rootId, string? targetFramework, string? runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(targetFramework) || string.IsNullOrWhiteSpace(runtimeIdentifier))
            throw new InvalidOperationException("A self-contained dependency lock requires an explicit target framework and runtime identifier.");
        var framework = targetFramework!.Trim().ToLowerInvariant();
        var packageId = "microsoft.netcore.app.runtime." + runtimeIdentifier!.Trim().ToLowerInvariant();
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var versionsRoot = Path.Combine(Path.GetFullPath(packageRoot!), packageId);
        if (!Directory.Exists(versionsRoot))
            throw new InvalidOperationException($"The reviewed runtime pack '{packageId}' is not restored in the configured NuGet package root.");
        var frameworkMajor = ParseFrameworkMajor(framework);
        var versionRoot = Directory.EnumerateDirectories(versionsRoot)
            .Select(path => new { Path = path, Version = ParsePackageVersion(Path.GetFileName(path)) })
            .Where(candidate => candidate.Version is not null && candidate.Version.Major == frameworkMajor)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
        if (versionRoot is null)
            throw new InvalidOperationException($"The reviewed runtime pack '{packageId}' has no restored version for '{framework}'.");
        var version = Path.GetFileName(versionRoot);
        var managedRoot = Path.Combine(versionRoot, "runtimes", runtimeIdentifier!, "lib", framework);
        if (!Directory.Exists(managedRoot))
            throw new InvalidOperationException($"The reviewed runtime pack '{packageId}/{version}' has no managed assets for '{framework}/{runtimeIdentifier}'.");

        foreach (var path in Directory.EnumerateFiles(managedRoot, "*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var hash = ComputeFileHash(path);
            var source = $"runtime-pack/{packageId}/{version}/{Path.GetFileName(path)}";
            var id = StableId("runtime-pack-managed", packageId.ToUpperInvariant(), version, Path.GetFileName(path).ToUpperInvariant(), hash);
            var identity = new PowerShellCompilationDependencyIdentity
            {
                Name = Path.GetFileName(path),
                Sha256 = hash,
                Source = source,
                TargetFramework = framework,
                RuntimeIdentifier = runtimeIdentifier!
            };
            ReadManagedIdentity(path, identity);
            identity.Provenance = "DotNetRuntimePack";
            _nodes.Add(id, new PowerShellCompilationDependencyNode
            {
                Id = id,
                Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                Roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                Identity = identity,
                Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored,
                Exists = true,
                Note = "Exact managed asset from the selected .NET runtime pack.",
                Policy = new PowerShellCompilationDependencyPolicy
                {
                    Redistribution = "DotNetRuntimePack",
                    Servicing = "ArtifactOwner"
                }
            });
            _pathNodes[path] = id;
            AddEdge(rootId, id, PowerShellCompilationDependencyEdgeKind.RuntimeAsset, source);
        }

        var nativeRoot = Path.Combine(versionRoot, "runtimes", runtimeIdentifier!, "native");
        if (!Directory.Exists(nativeRoot))
            throw new InvalidOperationException($"The reviewed runtime pack '{packageId}/{version}' has no native assets for '{runtimeIdentifier}'.");
        foreach (var path in Directory.EnumerateFiles(nativeRoot, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var hash = ComputeFileHash(path);
            var source = $"runtime-pack/{packageId}/{version}/native/{Path.GetFileName(path)}";
            var id = StableId("runtime-pack-native", packageId.ToUpperInvariant(), version, Path.GetFileName(path).ToUpperInvariant(), hash);
            _nodes.Add(id, new PowerShellCompilationDependencyNode
            {
                Id = id,
                Kind = PowerShellCompilationDependencyNodeKind.NativeLibrary,
                Roles = PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = Path.GetFileName(path),
                    Sha256 = hash,
                    Source = source,
                    TargetFramework = framework,
                    RuntimeIdentifier = runtimeIdentifier!,
                    Provenance = "DotNetRuntimePack"
                },
                Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored,
                Exists = true,
                Note = "Exact native asset from the selected .NET runtime pack.",
                Policy = new PowerShellCompilationDependencyPolicy
                {
                    Redistribution = "DotNetRuntimePack",
                    Servicing = "ArtifactOwner"
                }
            });
            _pathNodes[path] = id;
            AddEdge(rootId, id, PowerShellCompilationDependencyEdgeKind.RuntimeAsset, source);
        }
    }

    private static int ParseFrameworkMajor(string framework)
    {
        if (!framework.StartsWith("net", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(framework.Substring(3).Split('.')[0], out var major))
            throw new InvalidOperationException($"Runtime-pack locking does not recognize target framework '{framework}'.");
        return major;
    }

    private static Version? ParsePackageVersion(string value)
        => Version.TryParse(value.Split('-')[0], out var version) ? version : null;
}
