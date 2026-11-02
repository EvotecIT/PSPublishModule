using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static bool IsApiDocsFrameworkAssembly(Assembly assembly) =>
        !assembly.IsDynamic && string.Equals(Path.GetDirectoryName(assembly.Location),
            Path.GetDirectoryName(typeof(object).Assembly.Location), StringComparison.OrdinalIgnoreCase);

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
        private readonly IReadOnlyDictionary<string, string> _dependencyPaths;
        private readonly HashSet<string> _frameworkAssemblies;

        internal ApiDocsAssemblyLoadContext(string[] assemblyPaths)
            : base($"PowerForge.Web.ApiDocs:{Path.GetFileNameWithoutExtension(assemblyPaths[0])}", isCollectible: true)
        {
            var paths = assemblyPaths.Select(Path.GetFullPath).ToArray();
            _resolvers = paths.Select(path => new AssemblyDependencyResolver(path)).ToArray();
            _assemblyDirectories = paths.Select(path => Path.GetDirectoryName(path)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var dependencyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            foreach (var dependency in BuildDependencyPathMap(path))
                dependencyPaths.TryAdd(dependency.Key, dependency.Value);
            _dependencyPaths = dependencyPaths;

            // Only the shared runtime may come from the host. Application dependencies
            // must resolve from the documented outputs or their dependency manifests.
            _frameworkAssemblies = Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
                .Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public void Dispose() => Unload();

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblySimpleName = assemblyName.Name;
            if (assemblySimpleName is not null && _frameworkAssemblies.Contains(assemblySimpleName))
                return Default.LoadFromAssemblyName(assemblyName);

            foreach (var resolver in _resolvers)
            {
                var dependencyAssembly = TryLoadFromPath(resolver.ResolveAssemblyToPath(assemblyName));
                if (dependencyAssembly is not null)
                    return dependencyAssembly;
            }

            foreach (var directory in _assemblyDirectories)
            {
                var dependencyAssembly = TryLoadFromPath(Path.Combine(directory, assemblySimpleName + ".dll"));
                if (dependencyAssembly is not null)
                    return dependencyAssembly;
            }

            if (!string.IsNullOrWhiteSpace(assemblySimpleName) &&
                _dependencyPaths.TryGetValue(assemblySimpleName, out var mappedPath) &&
                File.Exists(mappedPath))
            {
                var mappedAssembly = TryLoadFromPath(mappedPath);
                if (mappedAssembly is not null)
                    return mappedAssembly;
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

            var packageRoots = GetNuGetPackageRootCandidates(assemblyPath)
                .Where(Directory.Exists)
                .ToArray();
            if (packageRoots.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var stream = File.OpenRead(depsPath);
            using var document = JsonDocument.Parse(stream);

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
                AddCandidate(Path.Combine(home, ".nuget", "packages"));

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
            var canonical = Path.Combine(directory, "NuGet.config");
            if (File.Exists(canonical))
                yield return canonical;

            var lowerCase = Path.Combine(directory, "nuget.config");
            if (!string.Equals(lowerCase, canonical, StringComparison.OrdinalIgnoreCase) && File.Exists(lowerCase))
                yield return lowerCase;
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
