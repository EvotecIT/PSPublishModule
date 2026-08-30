using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>Acquires or verifies an isolated exact package environment for every selected lock.</summary>
    public PowerShellCompilationProjectResult Restore(
        string projectPath,
        bool offline = false,
        IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var artifacts = SelectArtifacts(context, targetNames);
        var environmentRoot = context.Resolve(".powerforge/environment");
        var packageRoot = Path.Combine(environmentRoot, "packages");
        var httpCacheRoot = Path.Combine(environmentRoot, "http-cache");
        Directory.CreateDirectory(packageRoot);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        var verifiedPackages = new Dictionary<string, PowerShellCompilationProjectPackage>(StringComparer.OrdinalIgnoreCase);
        var resolvedLocks = new List<PowerShellCompilationProjectResolvedLock>();
        foreach (var artifact in artifacts)
        {
            try
            {
                var lockPath = context.Resolve(artifact.DependencyLock);
                if (!File.Exists(lockPath)) throw new FileNotFoundException("Run project lock before restore.", lockPath);
                var graph = ReadJson<PowerShellCompilationDependencyGraph>(lockPath);
                PowerShellCompilationDependencyLockHasher.EnsureValid(graph, artifact.Name);
                var packages = GetTargetPackages(graph, context, artifact);
                var restoreProject = WriteRestoreProject(environmentRoot, artifact, packages, offline);
                var restoreRoot = Path.GetDirectoryName(restoreProject)!;
                var resolvedLockPath = Path.Combine(restoreRoot, "packages.lock.json");
                if (offline && !File.Exists(resolvedLockPath))
                    throw new FileNotFoundException("Offline restore requires a previously acquired exact NuGet closure lock.", resolvedLockPath);
                var arguments = new List<string>
                {
                    "restore", restoreProject,
                    "--packages", packageRoot,
                    "--no-cache",
                    "--nologo", "--verbosity", "minimal"
                };
                if (offline)
                {
                    arguments.Add("--ignore-failed-sources");
                }
                if (File.Exists(resolvedLockPath)) arguments.Add("--locked-mode");
                if (!string.IsNullOrWhiteSpace(artifact.Target.RuntimeIdentifier) && artifact.Target.ArtifactKind == PowerShellCompilationArtifactKind.Executable)
                {
                    arguments.Add("--runtime");
                    arguments.Add(artifact.Target.RuntimeIdentifier);
                }
                var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                    "dotnet",
                    Path.GetDirectoryName(restoreProject)!,
                    arguments,
                    TimeSpan.FromMinutes(10),
                    new Dictionary<string, string?>
                    {
                        ["NUGET_PACKAGES"] = packageRoot,
                        ["NUGET_HTTP_CACHE_PATH"] = httpCacheRoot
                    })).GetAwaiter().GetResult();
                if (!run.Succeeded)
                    throw new InvalidOperationException($"Isolated restore failed for '{artifact.Name}': {run.StdOut}{Environment.NewLine}{run.StdErr}".Trim());
                if (!File.Exists(resolvedLockPath))
                    throw new FileNotFoundException("NuGet restore did not produce the required complete closure lock.", resolvedLockPath);
                var resolvedPackages = ReadResolvedPackages(resolvedLockPath);
                EnsureDeclaredPackagesAreLocked(packages, resolvedPackages, artifact.Name);
                VerifyAssetsClosure(Path.Combine(restoreRoot, "obj", "project.assets.json"), resolvedPackages, artifact.Name);
                foreach (var package in resolvedPackages)
                {
                    var verified = PowerShellCompilationNuGetPackageVerifier.Verify(packageRoot, package);
                    verifiedPackages[verified.Id + "/" + verified.Version] = verified;
                }
                resolvedLocks.Add(new PowerShellCompilationProjectResolvedLock
                {
                    TargetName = artifact.Name,
                    Path = FrameworkCompatibility.GetRelativePath(context.Root, resolvedLockPath).Replace('\\', '/'),
                    Sha256 = PowerShellCompilationProjectManifestService.ComputeSha256(resolvedLockPath)
                });
                VerifyRuntimeAssets(packageRoot, graph);
                results.Add(Pass(artifact, offline ? "Offline locked restore passed." : "Exact isolated acquisition passed.", packageRoot, graph.LockSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }

        var result = Complete("restore", context.ProjectPath, results);
        if (result.Succeeded)
        {
            var environment = new PowerShellCompilationProjectEnvironment
            {
                ProjectSha256 = PowerShellCompilationProjectManifestService.ComputeSha256(context.ProjectPath),
                PackageRoot = packageRoot,
                Offline = offline,
                DependencyLockSha256 = artifacts
                    .Select(artifact => ReadJson<PowerShellCompilationDependencyGraph>(context.Resolve(artifact.DependencyLock)).LockSha256)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray(),
                Packages = verifiedPackages.Values
                    .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static package => package.Version, StringComparer.Ordinal)
                    .ToArray(),
                ResolvedLocks = resolvedLocks.OrderBy(static item => item.TargetName, StringComparer.Ordinal).ToArray()
            };
            environment.EnvironmentSha256 = ComputeEnvironmentSha256(
                environment.ProjectSha256,
                environment.DependencyLockSha256,
                environment.Packages,
                environment.ResolvedLocks);
            WriteJson(Path.Combine(environmentRoot, "environment.json"), environment);
        }
        return result;
    }

    private static PowerShellCompilationProjectPackage[] GetTargetPackages(
        PowerShellCompilationDependencyGraph graph,
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact)
    {
        var packages = graph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NuGetPackage)
            .Select(static node => new PowerShellCompilationProjectPackage
            {
                Id = node.Identity.Name,
                Version = node.Identity.Version,
                ContentHash = node.Identity.ContentHash
            })
            .ToList();
        if (!string.IsNullOrWhiteSpace(artifact.ProviderLock))
        {
            var providerPath = context.Resolve(artifact.ProviderLock!);
            if (!File.Exists(providerPath)) throw new FileNotFoundException("Run project lock before restoring provider dependencies.", providerPath);
            var providerLock = ReadJson<PowerShellCompilationProviderLock>(providerPath);
            var expectedProviderHash = PowerShellCompilationProviderPackageReader.ComputeLockSha256(providerLock);
            if (!providerLock.LockSha256.Equals(expectedProviderHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Provider lock for '{artifact.Name}' does not match its recorded SHA-256.");
            packages.AddRange(providerLock.Packages.SelectMany(static package => package.Dependencies).Select(static dependency => new PowerShellCompilationProjectPackage
            {
                Id = dependency.PackageId,
                Version = dependency.Version,
                ContentHash = dependency.ContentHash
            }));
        }
        var conflicting = packages.GroupBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(static package => package.Version + "|" + package.ContentHash).Distinct(StringComparer.Ordinal).Count() > 1);
        if (conflicting is not null) throw new InvalidOperationException($"Project restore has incompatible package identities for '{conflicting.Key}'.");
        return packages
            .GroupBy(static package => package.Id + "/" + package.Version, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static string WriteRestoreProject(
        string environmentRoot,
        PowerShellCompilationProjectArtifact artifact,
        IEnumerable<PowerShellCompilationProjectPackage> packages,
        bool offline)
    {
        var root = Path.Combine(environmentRoot, "restore", artifact.Name);
        Directory.CreateDirectory(root);
        var packageItems = string.Join(Environment.NewLine, packages.Select(package =>
            $"    <PackageReference Include=\"{EscapeXml(package.Id)}\" Version=\"{EscapeXml(package.Version)}\" PrivateAssets=\"all\" />"));
        var rid = string.IsNullOrWhiteSpace(artifact.Target.RuntimeIdentifier)
            ? string.Empty
            : $"<RuntimeIdentifier>{EscapeXml(artifact.Target.RuntimeIdentifier)}</RuntimeIdentifier>";
        var project = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{EscapeXml(artifact.Target.TargetFramework)}</TargetFramework>
                {rid}
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                <NuGetLockFilePath>packages.lock.json</NuGetLockFilePath>
              </PropertyGroup>
              <ItemGroup>
            {packageItems}
              </ItemGroup>
            </Project>
            """;
        var projectPath = Path.Combine(root, "Restore.csproj");
        File.WriteAllText(projectPath, project + Environment.NewLine, new UTF8Encoding(false));
        var sources = offline
            ? "<clear />"
            : "<clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />";
        File.WriteAllText(
            Path.Combine(root, "NuGet.Config"),
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources>{sources}</packageSources></configuration>" + Environment.NewLine,
            new UTF8Encoding(false));
        return projectPath;
    }

    private static PowerShellCompilationProjectPackage[] ReadResolvedPackages(string lockPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("NuGet closure lock has no dependency groups.");
        var packages = new Dictionary<string, PowerShellCompilationProjectPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in dependencies.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var item in group.Value.EnumerateObject())
            {
                if (!item.Value.TryGetProperty("resolved", out var resolvedElement) ||
                    !item.Value.TryGetProperty("contentHash", out var hashElement))
                    throw new InvalidDataException($"NuGet closure entry '{item.Name}' is missing its exact version or content hash.");
                var version = resolvedElement.GetString() ?? string.Empty;
                var contentHash = PowerShellCompilationNuGetPackageVerifier.NormalizeContentHash(hashElement.GetString() ?? string.Empty);
                var key = item.Name + "/" + version;
                var package = new PowerShellCompilationProjectPackage { Id = item.Name, Version = version, ContentHash = contentHash };
                if (packages.TryGetValue(key, out var existing) &&
                    !existing.ContentHash.Equals(contentHash, StringComparison.Ordinal))
                    throw new InvalidDataException($"NuGet closure contains conflicting content identities for '{key}'.");
                packages[key] = package;
            }
        }
        return packages.Values
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnsureDeclaredPackagesAreLocked(
        IEnumerable<PowerShellCompilationProjectPackage> declared,
        IEnumerable<PowerShellCompilationProjectPackage> resolved,
        string targetName)
    {
        var actual = resolved.ToDictionary(
            static package => package.Id + "/" + package.Version,
            StringComparer.OrdinalIgnoreCase);
        foreach (var package in declared)
        {
            var key = package.Id + "/" + package.Version;
            if (!actual.TryGetValue(key, out var locked))
                throw new InvalidDataException($"NuGet closure for '{targetName}' omitted reviewed package '{key}'.");
            if (!PowerShellCompilationNuGetPackageVerifier.NormalizeContentHash(package.ContentHash).Equals(locked.ContentHash, StringComparison.Ordinal))
                throw new InvalidDataException($"NuGet closure for '{targetName}' changed the reviewed content identity of '{key}'.");
        }
    }

    private static void VerifyAssetsClosure(
        string assetsPath,
        IEnumerable<PowerShellCompilationProjectPackage> locked,
        string targetName)
    {
        if (!File.Exists(assetsPath)) throw new FileNotFoundException("NuGet restore did not produce an assets graph.", assetsPath);
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("NuGet assets graph has no libraries map.");
        var actual = libraries.EnumerateObject()
            .Where(static item => item.Value.TryGetProperty("type", out var type) &&
                                  (type.GetString() ?? string.Empty).Equals("package", StringComparison.OrdinalIgnoreCase))
            .Select(static item =>
            {
                var separator = item.Name.LastIndexOf('/');
                if (separator <= 0 || separator == item.Name.Length - 1)
                    throw new InvalidDataException($"NuGet assets package identity '{item.Name}' is malformed.");
                var hash = item.Value.TryGetProperty("sha512", out var hashElement) ? hashElement.GetString() ?? string.Empty : string.Empty;
                return new PowerShellCompilationProjectPackage
                {
                    Id = item.Name.Substring(0, separator),
                    Version = item.Name.Substring(separator + 1),
                    ContentHash = PowerShellCompilationNuGetPackageVerifier.NormalizeContentHash(hash)
                };
            })
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
        var expected = locked.OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToArray();
        if (actual.Length != expected.Length || actual.Where((package, index) =>
                !package.Id.Equals(expected[index].Id, StringComparison.OrdinalIgnoreCase) ||
                !package.Version.Equals(expected[index].Version, StringComparison.Ordinal) ||
                !package.ContentHash.Equals(PowerShellCompilationNuGetPackageVerifier.NormalizeContentHash(expected[index].ContentHash), StringComparison.Ordinal)).Any())
            throw new InvalidDataException($"NuGet assets closure for '{targetName}' differs from its exact packages.lock.json graph.");
    }

    private static void VerifyRuntimeAssets(string packageRoot, PowerShellCompilationDependencyGraph graph)
    {
        foreach (var node in graph.Nodes.Where(static node => node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal)))
        {
            var segments = node.Identity.Source.Split('/');
            if (segments.Length is not (4 or 5)) throw new InvalidDataException($"Runtime asset source '{node.Identity.Source}' is malformed.");
            var path = Path.Combine(
                packageRoot,
                segments[1],
                segments[2],
                "runtimes",
                node.Identity.RuntimeIdentifier,
                segments.Length == 5 ? "native" : "lib",
                segments.Length == 5 ? segments[4] : Path.Combine(node.Identity.TargetFramework, segments[3]));
            if (!File.Exists(path)) throw new FileNotFoundException("A reviewed runtime-pack asset is absent from the isolated environment.", path);
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            var actual = PowerShellCompilationProjectManifestService.ToHex(algorithm.ComputeHash(stream));
            if (!actual.Equals(node.Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Restored runtime-pack asset '{node.Identity.Source}' does not match its reviewed hash.");
        }
    }

    private static string ComputeEnvironmentSha256(
        string projectSha256,
        IEnumerable<string> locks,
        IEnumerable<PowerShellCompilationProjectPackage> packages,
        IEnumerable<PowerShellCompilationProjectResolvedLock> resolvedLocks)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            projectSha256,
            locks = locks.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            packages = packages.OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.Version, StringComparer.Ordinal)
                .Select(static package => new { package.Id, package.Version, package.ContentHash, package.ArchiveSha512, package.ExtractedFilesSha256 }),
            resolvedLocks = resolvedLocks.OrderBy(static item => item.TargetName, StringComparer.Ordinal)
                .Select(static item => new { item.TargetName, item.Path, item.Sha256 })
        });
        using var algorithm = SHA256.Create();
        return PowerShellCompilationProjectManifestService.ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
