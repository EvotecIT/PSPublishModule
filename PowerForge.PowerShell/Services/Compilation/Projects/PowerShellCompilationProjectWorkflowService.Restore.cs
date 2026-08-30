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
                foreach (var package in packages)
                {
                    VerifyPackage(packageRoot, package);
                    verifiedPackages[package.Id + "/" + package.Version] = package;
                }
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
                    .ToArray()
            };
            environment.EnvironmentSha256 = ComputeEnvironmentSha256(
                environment.ProjectSha256,
                environment.DependencyLockSha256,
                environment.Packages);
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

    private static void VerifyPackage(string packageRoot, PowerShellCompilationProjectPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Version) || string.IsNullOrWhiteSpace(package.ContentHash))
            throw new InvalidDataException("A project dependency lock contains an incomplete package identity.");
        var versionRoot = Path.Combine(packageRoot, package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
        var packagePath = Path.Combine(versionRoot, package.Id.ToLowerInvariant() + "." + package.Version.ToLowerInvariant() + ".nupkg");
        var hashPath = Path.Combine(versionRoot, package.Id.ToLowerInvariant() + "." + package.Version.ToLowerInvariant() + ".nupkg.sha512");
        var metadataPath = Path.Combine(versionRoot, ".nupkg.metadata");
        if (!File.Exists(packagePath) || !File.Exists(hashPath) || !File.Exists(metadataPath))
            throw new FileNotFoundException($"Exact restored package '{package.Id}/{package.Version}' is incomplete in the isolated environment.", versionRoot);
        using (var stream = File.OpenRead(packagePath))
        using (var algorithm = SHA512.Create())
        {
            var actualArchiveHash = Convert.ToBase64String(algorithm.ComputeHash(stream));
            var recordedArchiveHash = File.ReadAllText(hashPath).Trim();
            if (!actualArchiveHash.Equals(recordedArchiveHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Restored package archive '{package.Id}/{package.Version}' differs from its NuGet archive hash.");
        }
        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        if (!metadata.RootElement.TryGetProperty("contentHash", out var contentHashElement))
            throw new InvalidDataException($"Restored package '{package.Id}/{package.Version}' has no NuGet content identity.");
        var actualContentHash = contentHashElement.GetString() ?? string.Empty;
        if (!actualContentHash.Equals(package.ContentHash, StringComparison.Ordinal))
            throw new InvalidDataException($"Restored package '{package.Id}/{package.Version}' does not match the reviewed NuGet content hash (expected {package.ContentHash}, actual {actualContentHash}).");
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
        IEnumerable<PowerShellCompilationProjectPackage> packages)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            projectSha256,
            locks = locks.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            packages = packages.OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.Version, StringComparer.Ordinal)
                .Select(static package => new { package.Id, package.Version, package.ContentHash })
        });
        using var algorithm = SHA256.Create();
        return PowerShellCompilationProjectManifestService.ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
