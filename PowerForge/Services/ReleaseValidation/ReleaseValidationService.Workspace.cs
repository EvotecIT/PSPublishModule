using System.Xml.Linq;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private sealed class ValidationWorkspace : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PowerForge.Validation", Guid.NewGuid().ToString("N"));
        internal ValidationWorkspace() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            // Only this unique, task-owned directory is removed; never follow links created by a probe.
            ValidationDirectoryCleanup.TryDelete(Root);
        }
    }

    private static string Within(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.Length <= prefix.Length ||
            !FileSystemPathSafety.ExistingPathComparer.Equals(path.Substring(0, prefix.Length), prefix))
            throw new InvalidOperationException($"Path '{relative}' is outside '{root}'.");
        return path;
    }

    // Module payloads retain every directory; source consumers omit build and Git state.
    private static async Task CopyValidationDirectoryAsync(string source, string destination, CancellationToken cancellationToken,
        bool excludeBuildState = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(source);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Linked validation directory is not supported: {source}");
        Directory.CreateDirectory(destination);
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Linked validation input is not supported: {entry.FullName}");
            if (entry is DirectoryInfo child)
            {
                if (excludeBuildState && child.Name is ("bin" or "obj" or ".git")) continue;
                await CopyValidationDirectoryAsync(child.FullName, Path.Combine(destination, child.Name), cancellationToken, excludeBuildState).ConfigureAwait(false);
            }
            else await CopyValidationFileAsync(entry.FullName, Path.Combine(destination, entry.Name), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string WriteValidationNuGetConfig(string root, string feed, IEnumerable<string> ids, string[] dependencySources)
    {
        var sources = new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "staged"), new XAttribute("value", feed)));
        var mappings = new XElement("packageSourceMapping", new XElement("clear"),
            new XElement("packageSource", new XAttribute("key", "staged"),
                ids.Select(id => new XElement("package", new XAttribute("pattern", id)))));
        for (var index = 0; index < dependencySources.Length; index++)
        {
            var key = "dependency" + index;
            sources.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", dependencySources[index])));
            mappings.Add(new XElement("packageSource", new XAttribute("key", key), new XElement("package", new XAttribute("pattern", "*"))));
        }
        var path = Path.Combine(root, "NuGet.Config");
        new XDocument(new XElement("configuration", sources, mappings,
            new XElement("fallbackPackageFolders", new XElement("clear")))).Save(path);
        return path;
    }

    private async Task ValidateConsumerAsync(PackageConsumerValidation spec, Dictionary<string, PackageInspection> packages,
        bool sameVersion, Dictionary<string, string> variables, ReleaseValidationReport report, CancellationToken cancellationToken)
    {
        if (packages.Count == 0) throw new InvalidOperationException("Package consumers require a declared package set.");
        if (spec.Frameworks is null || spec.WindowsFrameworks is null ||
            spec.Frameworks.Concat(spec.WindowsFrameworks).Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Package consumer frameworks must be nonempty strings.");
        var frameworks = spec.Frameworks.Concat(FrameworkCompatibility.IsWindows() ? spec.WindowsFrameworks : Array.Empty<string>())
            .Select(framework => Expand(framework, variables)).Distinct().ToArray();
        if (frameworks.Length == 0 || frameworks.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Package consumer requires at least one executable framework.");
        using var workspace = new ValidationWorkspace();
        var projectRoot = Path.Combine(workspace.Root, "project");
        await CopyValidationDirectoryAsync(Resolve(spec.SourceDirectory, variables), projectRoot, cancellationToken, excludeBuildState: true).ConfigureAwait(false);
        var project = Within(projectRoot, spec.ProjectFile);
        if (!File.Exists(project)) throw new InvalidOperationException($"Smoke project does not exist: {project}");
        var feed = Directory.CreateDirectory(Path.Combine(workspace.Root, "feed")).FullName;
        foreach (var package in packages.Values)
            await CopyValidationFileAsync(package.Path, PackageFeedPath(feed, package), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var config = WriteValidationNuGetConfig(workspace.Root, feed, packages.Keys, spec.DependencySources);
        var cache = Path.Combine(workspace.Root, "packages");
        var values = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase) { ["WorkRoot"] = workspace.Root };
        var environment = new Dictionary<string, string?> { ["NUGET_PACKAGES"] = cache };
        // Mixed-version contracts leave version selection to the consumer's MSBuild project.
        // The restored archive hashes below still prove every selected staged version.
        var versionArguments = sameVersion ? new[] { "-p:PackageVersion=" + report.Version } : Array.Empty<string>();
        await RunCommandAsync(new ReleaseCommandValidation
        {
            Name = "Restore package consumer", FileName = "dotnet", WorkingDirectory = projectRoot, TimeoutSeconds = 600,
            Arguments = new[] { "restore", project, "--configfile", config, "--packages", cache, "--no-http-cache", "-p:Configuration=Release", "--nologo" }.Concat(versionArguments).ToArray(),
            Environment = environment
        }, values, report, cancellationToken, expandVariables: false).ConfigureAwait(false);
        foreach (var package in packages.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToLowerInvariant();
            var restored = Path.Combine(cache, id, version, id + "." + version + ".nupkg");
            var staged = PackageFeedPath(feed, package);
            if (!File.Exists(restored) ||
                await DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(restored, cancellationToken).ConfigureAwait(false) !=
                await DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(staged, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"Consumer did not restore the exact staged '{package.Id}' package.");
        }
        foreach (var framework in frameworks)
            await RunCommandAsync(new ReleaseCommandValidation
            {
                Name = "Run package consumer " + framework, FileName = "dotnet", WorkingDirectory = projectRoot, TimeoutSeconds = 600,
                Arguments = new[] { "run", "--project", project, "--framework", framework, "--configuration", "Release", "--no-restore", "--nologo" }.Concat(versionArguments).ToArray(),
                Environment = environment
            }, values, report, cancellationToken, expandVariables: false).ConfigureAwait(false);
    }
}
