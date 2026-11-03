using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static readonly Lazy<Dictionary<string, string>> ApiDocsFrameworkAssemblyPaths = new(GetApiDocsFrameworkAssemblyPaths);

    private static bool IsApiDocsFrameworkAssembly(Assembly assembly) =>
        assembly == typeof(object).Assembly ||
        (!assembly.IsDynamic && ApiDocsFrameworkAssemblyPaths.Value.TryGetValue(assembly.GetName().Name!, out var path) &&
         string.Equals(assembly.Location, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Uses the host's runtime-pack manifests; self-contained outputs also contain application DLLs.</summary>
    private static Dictionary<string, string> GetApiDocsFrameworkAssemblyPaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The runtime uses semicolons on every platform, including Unix.
        var depsFiles = (AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var depsFile in depsFiles.Where(File.Exists))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(depsFile));
            if (!document.RootElement.TryGetProperty("targets", out var targets)) continue;
            foreach (var target in targets.EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Name.StartsWith("Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase) &&
                    !library.Name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase)) continue;
                if (!library.Value.TryGetProperty("runtime", out var assets)) continue;
                foreach (var asset in assets.EnumerateObject())
                    if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = Path.Combine(Path.GetDirectoryName(depsFile)!, Path.GetFileName(asset.Name));
                        if (File.Exists(path)) paths.TryAdd(Path.GetFileNameWithoutExtension(asset.Name), path);
                    }
            }
        }
        return paths;
    }

    private static Assembly? TryLoadAssembly(string[] assemblyPaths, List<string> warnings, out ApiDocsAssemblyLoadContext? context)
    {
        var fullPath = Path.GetFullPath(assemblyPaths[0]);
        context = null;
        try
        {
            context = new ApiDocsAssemblyLoadContext(assemblyPaths);
            using var stream = new MemoryStream(File.ReadAllBytes(fullPath), writable: false);
            return context.LoadFromStream(stream);
        }
        catch (Exception ex)
        {
            context?.Dispose();
            context = null;
            warnings.Add($"Assembly load failed: {Path.GetFileName(fullPath)} ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    internal static IReadOnlyList<string> GetApiDocsNuGetPackageRootCandidates()
    {
        return ApiDocsAssemblyLoadContext.GetNuGetPackageRootCandidates();
    }

    internal static IReadOnlyList<string> GetApiDocsNuGetPackageRootCandidates(string assemblyPath)
    {
        return ApiDocsAssemblyLoadContext.GetNuGetPackageRootCandidates(assemblyPath);
    }

    private sealed class ApiDocsAssemblyLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly AssemblyDependencyResolver[] _resolvers;
        private readonly string[] _assemblyDirectories;
        private readonly IReadOnlyDictionary<string, string>[] _dependencyPaths;

        internal ApiDocsAssemblyLoadContext(string[] assemblyPaths)
            : base($"PowerForge.Web.ApiDocs:{Path.GetFileNameWithoutExtension(assemblyPaths[0])}", isCollectible: true)
        {
            var paths = assemblyPaths.Select(Path.GetFullPath).ToArray();
            _assemblyDirectories = paths.Select(path => Path.GetDirectoryName(path)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _dependencyPaths = paths.Select(BuildDependencyPathMap).ToArray();
            _resolvers = paths.Select(path => new AssemblyDependencyResolver(path)).ToArray();
        }

        public void Dispose() => Unload();

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblySimpleName = assemblyName.Name;
            if (assemblySimpleName == typeof(object).Assembly.GetName().Name)
                return typeof(object).Assembly;

            var paths = _resolvers.Select(resolver => resolver.ResolveAssemblyToPath(assemblyName))
                .Concat(_assemblyDirectories.Select(directory => Path.Combine(directory, assemblySimpleName + ".dll")))
                .Concat(_dependencyPaths.Select(map => assemblySimpleName is not null && map.TryGetValue(assemblySimpleName, out var path) ? path : null))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<(string Path, Version? Version)>();
            foreach (var path in paths)
            {
                var candidateName = ReadCandidateName(path, assemblyName);
                if (candidateName is not null) candidates.Add((path, candidateName.Version));
            }

            // One context can hold only one version per name. Select the newest compatible
            // project candidate even when the first inspected assembly requests an older one.
            foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Version))
            {
                var dependency = TryLoadFromPath(candidate.Path);
                if (dependency is not null) return dependency;
            }

            if (assemblySimpleName is not null && ApiDocsFrameworkAssemblyPaths.Value.TryGetValue(assemblySimpleName, out var runtimePath) &&
                ReadCandidateName(runtimePath, assemblyName) is { } runtimeName)
            {
                try
                {
                    var shared = Default.LoadFromAssemblyName(runtimeName);
                    if (IsApiDocsFrameworkAssembly(shared)) return shared;
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
                {
                    // An older host package can also prevent a default-context bind.
                }
                // A host package can override a runtime-shipped library. Never borrow it.
                var runtimeAssembly = TryLoadFromPath(runtimePath);
                if (runtimeAssembly is not null) return runtimeAssembly;
            }

            // Returning null would silently fall back to the CLI's default context.
            throw new FileNotFoundException($"API documentation dependency '{assemblyName}' was not found in the documented project outputs or dependency manifests.", assemblySimpleName + ".dll");
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            foreach (var resolver in _resolvers)
            {
                var unmanagedDllPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (!string.IsNullOrWhiteSpace(unmanagedDllPath))
                    return LoadUnmanagedDllFromPath(unmanagedDllPath);
            }

            return IntPtr.Zero;
        }

        private static AssemblyName? ReadCandidateName(string path, AssemblyName requestedName)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var candidateName = AssemblyName.GetAssemblyName(path);
                return AssemblyName.ReferenceMatchesDefinition(requestedName, candidateName) &&
                    (requestedName.Version is null || candidateName.Version >= requestedName.Version) ? candidateName : null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return null;
            }
        }

        private Assembly? TryLoadFromPath(string? candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                return null;

            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(candidatePath), writable: false);
                return LoadFromStream(stream);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        private static IReadOnlyDictionary<string, string> BuildDependencyPathMap(string assemblyPath)
        {
            var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
            if (!File.Exists(depsPath))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var document = ReadDependencyManifest(depsPath);
            var packageRoots = GetNuGetPackageRootCandidates(assemblyPath)
                .Where(Directory.Exists)
                .ToArray();
            if (packageRoots.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!document.RootElement.TryGetProperty("libraries", out var librariesElement) ||
                librariesElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("targets", out var targetsElement) ||
                targetsElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var packagePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in librariesElement.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (!library.Value.TryGetProperty("type", out var typeElement) ||
                    !string.Equals(typeElement.GetString(), "package", StringComparison.OrdinalIgnoreCase) ||
                    !library.Value.TryGetProperty("path", out var pathElement))
                {
                    continue;
                }

                var relativePackagePath = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(relativePackagePath))
                    packagePaths[library.Name] = relativePackagePath.Replace('/', Path.DirectorySeparatorChar);
            }

            var dependencyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targetsElement.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var library in target.Value.EnumerateObject())
                {
                    if (!packagePaths.TryGetValue(library.Name, out var relativePackagePath) ||
                        library.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var packageRoot in packageRoots)
                        AddDependencyPaths(library.Value, relativePackagePath, packageRoot, dependencyPaths);
                }
            }

            return dependencyPaths;
        }

        private static JsonDocument ReadDependencyManifest(string depsPath)
        {
            try
            {
                using var stream = File.OpenRead(depsPath);
                return JsonDocument.Parse(stream);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"API documentation dependency manifest could not be parsed: {depsPath}. {ex.Message}", ex);
            }
        }

        private static void AddDependencyPaths(
            JsonElement libraryElement,
            string relativePackagePath,
            string packageRoot,
            Dictionary<string, string> dependencyPaths)
        {
            AddDependencyPathsFromSection("runtime");
            AddDependencyPathsFromSection("runtimeTargets");

            void AddDependencyPathsFromSection(string sectionName)
            {
                if (!libraryElement.TryGetProperty(sectionName, out var runtimeElement) ||
                    runtimeElement.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                foreach (var runtimeAsset in runtimeElement.EnumerateObject())
                {
                    if (!runtimeAsset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fullPath = Path.Combine(packageRoot, relativePackagePath, runtimeAsset.Name.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                        continue;

                    var assemblyName = Path.GetFileNameWithoutExtension(runtimeAsset.Name);
                    if (!dependencyPaths.ContainsKey(assemblyName))
                        dependencyPaths[assemblyName] = fullPath;
                }
            }
        }

        internal static IReadOnlyList<string> GetNuGetPackageRootCandidates()
        {
            return GetNuGetPackageRootCandidates(null);
        }

        internal static IReadOnlyList<string> GetNuGetPackageRootCandidates(string? assemblyPath)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath) && seen.Add(fullPath))
                        candidates.Add(fullPath);
                }
                catch
                {
                    // Ignore invalid candidate paths.
                }
            }

            AddCandidate(Environment.GetEnvironmentVariable("NUGET_PACKAGES"));

            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                foreach (var directory in EnumerateAssemblyProbeDirectories(assemblyPath))
                {
                    AddCandidate(Path.Combine(directory, ".nuget", "packages"));

                    foreach (var configPath in EnumerateNuGetConfigPaths(directory))
                        AddCandidate(GetConfiguredPackageRoot(configPath));
                }
            }

            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                AddCandidate(GetConfiguredPackageRoot(Path.Combine(home, ".config", "NuGet", "NuGet.Config")));
                AddCandidate(GetConfiguredPackageRoot(Path.Combine(home, ".nuget", "NuGet", "NuGet.Config")));
                AddCandidate(Path.Combine(home, ".nuget", "packages"));
            }

            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrWhiteSpace(appData))
                AddCandidate(GetConfiguredPackageRoot(Path.Combine(appData, "NuGet", "NuGet.Config")));

            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(userProfile))
                AddCandidate(Path.Combine(userProfile, ".nuget", "packages"));

            var homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            var homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
                AddCandidate(Path.Combine(homeDrive + homePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar), ".nuget", "packages"));

            var specialFolderProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(specialFolderProfile))
                AddCandidate(Path.Combine(specialFolderProfile, ".nuget", "packages"));

            return candidates;
        }

        private static IEnumerable<string> EnumerateAssemblyProbeDirectories(string assemblyPath)
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            while (!string.IsNullOrWhiteSpace(current))
            {
                yield return current;

                var parent = Directory.GetParent(current);
                if (parent is null)
                    yield break;

                current = parent.FullName;
            }
        }

        private static IEnumerable<string> EnumerateNuGetConfigPaths(string directory)
        {
            foreach (var name in new[] { "NuGet.Config", "NuGet.config", "nuget.config" })
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) yield return path;
            }
        }

        private static string? GetConfiguredPackageRoot(string configPath)
        {
            try
            {
                var document = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
                var configuredFolder = document.Root?
                    .Element("config")?
                    .Elements("add")
                    .FirstOrDefault(element =>
                        string.Equals((string?)element.Attribute("key"), "globalPackagesFolder", StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("value")
                    ?.Value;

                if (string.IsNullOrWhiteSpace(configuredFolder))
                    return null;

                return Path.IsPathRooted(configuredFolder)
                    ? configuredFolder
                    : Path.Combine(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory, configuredFolder);
            }
            catch
            {
                return null;
            }
        }
    }
}
