namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static string BuildAssemblyLoadContextSource(AssemblyLoadContextLoaderIdentity identity)
        => $@"using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace {identity.Namespace};

public sealed class ModuleAssemblyLoadContext : AssemblyLoadContext
{{
    private static readonly object Sync = new();
    // Module contexts are intentionally non-collectible. A process restart is required to load a replaced DLL at the same path.
    private static readonly Dictionary<string, ModuleAssemblyLoadContext> Contexts = new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _assemblyDirectories;
    private readonly AssemblyDependencyResolver[] _resolvers;
    private readonly DependencyManifestResolver[] _manifestResolvers;
    private readonly Dictionary<string, Assembly> _moduleAssemblies = new(StringComparer.OrdinalIgnoreCase);

    private ModuleAssemblyLoadContext(string[] moduleAssemblyPaths, string contextName)
        : base(contextName, isCollectible: false)
    {{
        _assemblyDirectories = moduleAssemblyPaths
            .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _resolvers = moduleAssemblyPaths
            .Select(TryCreateResolver)
            .OfType<AssemblyDependencyResolver>()
            .ToArray();
        _manifestResolvers = moduleAssemblyPaths
            .Select(DependencyManifestResolver.TryCreate)
            .OfType<DependencyManifestResolver>()
            .ToArray();
    }}

    public static Assembly LoadModule(string moduleAssemblyPath, string? contextName)
        => LoadModules(new[] {{ moduleAssemblyPath }}, contextName)[0];

    public static Assembly LoadModuleFromGroup(string[] moduleAssemblyPaths, string moduleAssemblyPath, string? contextName)
    {{
        var fullPaths = NormalizeModuleAssemblyPaths(moduleAssemblyPaths);
        var requestedPath = Path.GetFullPath(moduleAssemblyPath ?? throw new ArgumentNullException(nameof(moduleAssemblyPath)));
        if (!fullPaths.Contains(requestedPath, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(""The requested module assembly must belong to the configured module group."", nameof(moduleAssemblyPath));

        var contextKey = string.Join(""|"", fullPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        lock (Sync)
        {{
            var context = GetOrCreateContext(fullPaths, contextKey, contextName);
            return context.LoadModuleAssembly(requestedPath);
        }}
    }}

    public static Assembly[] LoadModules(string[] moduleAssemblyPaths, string? contextName)
    {{
        var fullPaths = NormalizeModuleAssemblyPaths(moduleAssemblyPaths);
        var contextKey = string.Join(""|"", fullPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        // The global lock keeps context creation and all configured export assembly loads single-shot for each module group.
        lock (Sync)
        {{
            var context = GetOrCreateContext(fullPaths, contextKey, contextName);
            return fullPaths.Select(context.LoadModuleAssembly).ToArray();
        }}
    }}

    private static string[] NormalizeModuleAssemblyPaths(string[] moduleAssemblyPaths)
    {{
        if (moduleAssemblyPaths is null || moduleAssemblyPaths.Length == 0)
            throw new ArgumentException(""At least one module assembly path is required."", nameof(moduleAssemblyPaths));

        var fullPaths = moduleAssemblyPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (fullPaths.Length == 0)
            throw new ArgumentException(""At least one module assembly path is required."", nameof(moduleAssemblyPaths));
        foreach (var fullPath in fullPaths)
        {{
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(""Module assembly was not found."", fullPath);
        }}

        return fullPaths;
    }}

    private static ModuleAssemblyLoadContext GetOrCreateContext(string[] fullPaths, string contextKey, string? contextName)
    {{
        if (Contexts.TryGetValue(contextKey, out var context))
            return context;

        context = new ModuleAssemblyLoadContext(fullPaths, string.IsNullOrWhiteSpace(contextName) ? Path.GetFileNameWithoutExtension(fullPaths[0]) : contextName);
        Contexts[contextKey] = context;
        return context;
    }}

    protected override Assembly? Load(AssemblyName assemblyName)
    {{
        if (assemblyName is null || string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        var loaderAssembly = typeof(ModuleAssemblyLoadContext).Assembly.GetName();
        if (AssemblyName.ReferenceMatchesDefinition(loaderAssembly, assemblyName))
            return typeof(ModuleAssemblyLoadContext).Assembly;

        foreach (var resolver in _resolvers)
        {{
            var resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {{
                // A package can place a compile-time facade beside the module and the real
                // implementation under runtimes/<rid>/lib. Replace only that adjacent
                // facade; preserve every non-adjacent path selected by the dependency resolver.
                var runtimePath = IsAdjacentAssemblyPath(resolvedPath, assemblyName.Name)
                    ? ResolvePackagedRuntimeAssembly(assemblyName.Name)
                    : null;
                if (!string.IsNullOrWhiteSpace(runtimePath) && File.Exists(runtimePath))
                    return LoadFromAssemblyPath(runtimePath);

                return LoadFromAssemblyPath(resolvedPath);
            }}
        }}

        foreach (var manifestResolver in _manifestResolvers)
        {{
            var resolvedPath = manifestResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadFromAssemblyPath(resolvedPath);
        }}

        var packagedRuntimePath = ResolvePackagedRuntimeAssembly(assemblyName.Name);
        if (!string.IsNullOrWhiteSpace(packagedRuntimePath) && File.Exists(packagedRuntimePath))
            return LoadFromAssemblyPath(packagedRuntimePath);

        foreach (var assemblyDirectory in _assemblyDirectories)
        {{
            var assemblyPath = Path.Combine(assemblyDirectory, assemblyName.Name + "".dll"");
            if (File.Exists(assemblyPath))
                return LoadFromAssemblyPath(assemblyPath);
        }}

        return null;
    }}

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {{
        foreach (var resolver in _resolvers)
        {{
            var resolvedPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);
        }}

        foreach (var manifestResolver in _manifestResolvers)
        {{
            var resolvedPath = manifestResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);
        }}

        var packagedLibrary = LoadPackagedNativeLibrary(unmanagedDllName);
        return packagedLibrary != IntPtr.Zero
            ? packagedLibrary
            : IntPtr.Zero;
    }}

    private Assembly LoadModuleAssembly(string moduleAssemblyPath)
    {{
        // Called only while LoadModules holds Sync; keep every configured export in the same context.
        var fullPath = Path.GetFullPath(moduleAssemblyPath);
        if (_moduleAssemblies.TryGetValue(fullPath, out var loaded))
            return loaded;

        loaded = Assemblies.FirstOrDefault(assembly =>
            !assembly.IsDynamic &&
            !string.IsNullOrWhiteSpace(assembly.Location) &&
            string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase))
            ?? LoadFromAssemblyPath(fullPath);
        _moduleAssemblies[fullPath] = loaded;
        return loaded;
    }}

    private static AssemblyDependencyResolver? TryCreateResolver(string assemblyPath)
    {{
        try
        {{
            return new AssemblyDependencyResolver(assemblyPath);
        }}
        catch (InvalidOperationException)
        {{
            return null;
        }}
    }}

    private sealed class DependencyManifestResolver
    {{
        private readonly string _assemblyDirectory;
        private readonly JsonElement _target;
        private readonly string[] _runtimeIdentifiers;

        private DependencyManifestResolver(string assemblyPath, JsonElement target)
        {{
            _assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            _target = target;
            _runtimeIdentifiers = BuildRuntimeIdentifiers();
        }}

        public static DependencyManifestResolver? TryCreate(string assemblyPath)
        {{
            var depsPath = Path.ChangeExtension(assemblyPath, "".deps.json"");
            if (string.IsNullOrWhiteSpace(depsPath) || !File.Exists(depsPath))
                return null;

            try
            {{
                var document = JsonDocument.Parse(File.ReadAllText(depsPath));
                if (!document.RootElement.TryGetProperty(""targets"", out var targets) || targets.ValueKind != JsonValueKind.Object)
                {{
                    document.Dispose();
                    return null;
                }}

                JsonElement target;
                if (document.RootElement.TryGetProperty(""runtimeTarget"", out var runtimeTarget) &&
                    runtimeTarget.TryGetProperty(""name"", out var targetName) &&
                    targetName.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(targetName.GetString()) &&
                    targets.TryGetProperty(targetName.GetString()!, out target))
                {{
                    var clonedTarget = target.Clone();
                    document.Dispose();
                    return new DependencyManifestResolver(assemblyPath, clonedTarget);
                }}

                foreach (var candidate in targets.EnumerateObject())
                {{
                    var clonedTarget = candidate.Value.Clone();
                    document.Dispose();
                    return new DependencyManifestResolver(assemblyPath, clonedTarget);
                }}

                document.Dispose();
                return null;
            }}
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidOperationException)
            {{
                return null;
            }}
        }}

        public string? ResolveAssemblyToPath(AssemblyName assemblyName)
        {{
            if (assemblyName is null || string.IsNullOrWhiteSpace(assemblyName.Name))
                return null;

            var resolved = SearchRuntimeTargets(assemblyName.Name, ""runtime"");
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return SearchAssetGroup(assemblyName.Name, ""runtime"");
        }}

        public string? ResolveUnmanagedDllToPath(string unmanagedDllName)
        {{
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
                return null;

            var names = new HashSet<string>(GetNativeLibraryFileNames(unmanagedDllName), StringComparer.OrdinalIgnoreCase);
            var resolved = SearchRuntimeTargets(names, ""native"");
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return SearchAssetGroup(names, ""native"");
        }}

        private string? SearchRuntimeTargets(string assemblyName, string assetType)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(""runtimeTargets"", out var runtimeTargets) ||
                    runtimeTargets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var rid in _runtimeIdentifiers)
                {{
                    foreach (var asset in runtimeTargets.EnumerateObject())
                    {{
                        if (!asset.Value.TryGetProperty(""assetType"", out var declaredAssetType) ||
                            !string.Equals(declaredAssetType.GetString(), assetType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!asset.Value.TryGetProperty(""rid"", out var declaredRid) ||
                            !string.Equals(declaredRid.GetString(), rid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(Path.GetFileNameWithoutExtension(asset.Name), assemblyName, StringComparison.OrdinalIgnoreCase))
                        {{
                            var resolved = ResolveAssetPath(asset.Name);
                            if (!string.IsNullOrWhiteSpace(resolved))
                                return resolved;
                        }}
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchRuntimeTargets(HashSet<string> fileNames, string assetType)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(""runtimeTargets"", out var runtimeTargets) ||
                    runtimeTargets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var rid in _runtimeIdentifiers)
                {{
                    foreach (var asset in runtimeTargets.EnumerateObject())
                    {{
                        if (!asset.Value.TryGetProperty(""assetType"", out var declaredAssetType) ||
                            !string.Equals(declaredAssetType.GetString(), assetType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!asset.Value.TryGetProperty(""rid"", out var declaredRid) ||
                            !string.Equals(declaredRid.GetString(), rid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (fileNames.Contains(Path.GetFileName(asset.Name)))
                        {{
                            var resolved = ResolveAssetPath(asset.Name);
                            if (!string.IsNullOrWhiteSpace(resolved))
                                return resolved;
                        }}
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchAssetGroup(string assemblyName, string groupName)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(groupName, out var assets) || assets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var asset in assets.EnumerateObject())
                {{
                    if (string.Equals(Path.GetFileNameWithoutExtension(asset.Name), assemblyName, StringComparison.OrdinalIgnoreCase))
                    {{
                        var resolved = ResolveAssetPath(asset.Name);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved;
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchAssetGroup(HashSet<string> fileNames, string groupName)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(groupName, out var assets) || assets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var asset in assets.EnumerateObject())
                {{
                    if (fileNames.Contains(Path.GetFileName(asset.Name)))
                    {{
                        var resolved = ResolveAssetPath(asset.Name);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved;
                    }}
                }}
            }}

            return null;
        }}

        private string? ResolveAssetPath(string assetPath)
        {{
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            var normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            foreach (var candidate in new[]
            {{
                Path.Combine(_assemblyDirectory, normalized),
                Path.Combine(_assemblyDirectory, Path.GetFileName(normalized))
            }})
            {{
                if (File.Exists(candidate))
                    return candidate;
            }}

            return null;
        }}

        private static string[] BuildRuntimeIdentifiers()
        {{
            var values = new List<string>();
            foreach (var rid in GetRuntimeIdentifiers())
            {{
                if (!string.IsNullOrWhiteSpace(rid) && !values.Contains(rid, StringComparer.OrdinalIgnoreCase))
                    values.Add(rid);
            }}

            values.Add(string.Empty);
            return values.ToArray();
        }}
    }}

    private string? ResolvePackagedRuntimeAssembly(string assemblyName)
    {{
        if (string.IsNullOrWhiteSpace(assemblyName))
            return null;

        var fileName = assemblyName + "".dll"";
        foreach (var rid in GetRuntimeIdentifiers())
        {{
            foreach (var assemblyDirectory in _assemblyDirectories)
            {{
                var runtimeLibRoot = Path.Combine(assemblyDirectory, ""runtimes"", rid, ""lib"");
                if (!Directory.Exists(runtimeLibRoot))
                    continue;

                try
                {{
                    foreach (var path in Directory.EnumerateFiles(runtimeLibRoot, fileName, SearchOption.AllDirectories))
                    {{
                        if (File.Exists(path))
                            return path;
                    }}
                }}
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                {{
                    continue;
                }}
            }}
        }}

        return null;
    }}

    private bool IsAdjacentAssemblyPath(string resolvedPath, string assemblyName)
    {{
        if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(assemblyName))
            return false;

        var fullResolvedPath = Path.GetFullPath(resolvedPath);
        return _assemblyDirectories.Any(assemblyDirectory =>
            string.Equals(
                fullResolvedPath,
                Path.GetFullPath(Path.Combine(assemblyDirectory, assemblyName + "".dll"")),
                StringComparison.OrdinalIgnoreCase));
    }}

    private IntPtr LoadPackagedNativeLibrary(string unmanagedDllName)
    {{
        if (string.IsNullOrWhiteSpace(unmanagedDllName))
            return IntPtr.Zero;

        foreach (var rid in GetRuntimeIdentifiers())
        {{
            foreach (var assemblyDirectory in _assemblyDirectories)
            {{
                foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
                {{
                    var path = Path.Combine(assemblyDirectory, ""runtimes"", rid, ""native"", fileName);
                    if (File.Exists(path))
                    {{
                        var loaded = TryLoadPackagedNativeLibrary(path);
                        if (loaded != IntPtr.Zero)
                            return loaded;
                    }}
                }}
            }}
        }}

        foreach (var assemblyDirectory in _assemblyDirectories)
        {{
            foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
            {{
                var path = Path.Combine(assemblyDirectory, fileName);
                if (File.Exists(path))
                {{
                    var loaded = TryLoadPackagedNativeLibrary(path);
                    if (loaded != IntPtr.Zero)
                        return loaded;
                }}
            }}
        }}

        return IntPtr.Zero;
    }}

    private IntPtr TryLoadPackagedNativeLibrary(string path)
    {{
        try
        {{
            return LoadUnmanagedDllFromPath(path);
        }}
        catch (Exception ex) when (ex is BadImageFormatException || ex is DllNotFoundException || ex is FileLoadException)
        {{
            return IntPtr.Zero;
        }}
    }}

    private static IEnumerable<string> GetRuntimeIdentifiers()
    {{
        var runtimeIdentifier = typeof(RuntimeInformation)
            .GetProperty(""RuntimeIdentifier"", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
            yield return runtimeIdentifier;

        var arch = RuntimeInformation.ProcessArchitecture switch
        {{
            Architecture.X64 => ""x64"",
            Architecture.X86 => ""x86"",
            Architecture.Arm64 => ""arm64"",
            Architecture.Arm => ""arm"",
            _ => null
        }};
        var isMusl = runtimeIdentifier.Contains(""musl"", StringComparison.OrdinalIgnoreCase);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {{
            if (arch is not null)
                yield return ""win-"" + arch;
            yield return ""win"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {{
            if (arch is not null)
                yield return ""osx-"" + arch;
            yield return ""osx"";
            yield return ""unix"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {{
            if (arch is not null)
            {{
                if (isMusl)
                {{
                    yield return ""linux-musl-"" + arch;
                    yield return ""linux-musl"";
                    yield return ""linux-"" + arch;
                }}
                else
                {{
                    yield return ""linux-"" + arch;
                    yield return ""linux-musl-"" + arch;
                    yield return ""linux-musl"";
                }}
            }}
            yield return ""linux"";
            yield return ""unix"";
        }}
    }}

    private static IEnumerable<string> GetNativeLibraryFileNames(string unmanagedDllName)
    {{
        yield return unmanagedDllName;

        if (Path.HasExtension(unmanagedDllName))
            yield break;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {{
            yield return unmanagedDllName + "".dll"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {{
            yield return unmanagedDllName + "".dylib"";
            if (!unmanagedDllName.StartsWith(""lib"", StringComparison.Ordinal))
                yield return ""lib"" + unmanagedDllName + "".dylib"";
        }}
        else
        {{
            // Most non-Windows, non-macOS PowerShell hosts use ELF shared objects, so .so is the safest portable fallback.
            yield return unmanagedDllName + "".so"";
            if (!unmanagedDllName.StartsWith(""lib"", StringComparison.Ordinal))
                yield return ""lib"" + unmanagedDllName + "".so"";
        }}
    }}
}}
";

}
