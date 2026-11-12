using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
#if NETCOREAPP
using System.Runtime.Loader;
#endif

namespace PowerForge.ModuleLoader.V1;

/// <summary>
/// Registers PowerShell type accelerators for configured module types and removes them again on
/// Remove-Module. PowerShell keeps its accelerator table on an internal type, so this compiled
/// helper owns the reflection that generated scripts must not contain.
/// </summary>
/// <remarks>
/// One accelerator name maps to one type per process. A name another module already registered stays
/// with that module; this registration takes it over when that module is removed.
/// </remarks>
public sealed class TypeAcceleratorRegistration
{
    // Shared with modules built by earlier PowerForge releases, which kept the same owner catalog
    // shape from script: a synchronized table of { Type, Owners } entries in AppDomain data.
    private const string OwnersKey = "PowerForge.TypeAcceleratorOwners.v1";

    // Live registrations in import order, shared by every module that uses this loader version.
    private static readonly List<TypeAcceleratorRegistration> Live = new();

    private readonly string _ownerId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, Type> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Type> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _keptByOthers = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _verbose = new();
    private readonly AcceleratorTable _table;
    private string _module = string.Empty;

    private TypeAcceleratorRegistration(AcceleratorTable table) => _table = table;

    /// <summary>Messages the calling script should surface with Write-Warning.</summary>
    public string[] Warnings => _warnings.ToArray();

    /// <summary>Messages the calling script should surface with Write-Verbose.</summary>
    public string[] VerboseMessages => _verbose.ToArray();

    /// <summary>Accelerator names this registration added or co-owns.</summary>
    public string[] Names => _registered.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

#if NETCOREAPP
    /// <summary>Registers types resolved from the module's AssemblyLoadContext (PowerShell 7).</summary>
    public static TypeAcceleratorRegistration RegisterFromLoadContext(
        Assembly moduleAssembly,
        string libraryDirectory,
        string mode,
        string[]? typeNames,
        string[]? assemblyNames,
        string moduleName,
        string? moduleRoot)
    {
        var registration = Create(out var table, moduleName, moduleRoot);
        if (table is null) return registration;
        if (moduleAssembly is null)
            return registration.Warn("Module assembly was not available. ALC dependency type exposure is disabled.");
        if (string.IsNullOrWhiteSpace(libraryDirectory))
            return registration.Warn("Module library folder was not available. ALC dependency type exposure is disabled.");

        var context = AssemblyLoadContext.GetLoadContext(moduleAssembly);
        if (context is null)
            return registration.Warn("Unable to resolve the module AssemblyLoadContext. ALC dependency type exposure is disabled.");

        var source = new LoadContextTypeSource(context, Path.GetFullPath(libraryDirectory), registration);
        registration.Register(mode, typeNames, assemblyNames, source, "ALC");
        return registration;
    }
#endif

    /// <summary>Registers types from assemblies loaded out of the module library directory (Windows PowerShell).</summary>
    public static TypeAcceleratorRegistration RegisterFromDirectory(
        string libraryDirectory,
        string mode,
        string[]? typeNames,
        string[]? assemblyNames,
        string[]? ignoredLibraryFileNames,
        string moduleName,
        string? moduleRoot)
    {
        var registration = Create(out var table, moduleName, moduleRoot);
        if (table is null) return registration;
        if (string.IsNullOrWhiteSpace(libraryDirectory) || !Directory.Exists(libraryDirectory))
            return registration.Warn("Module library directory was not available. Desktop dependency type exposure is disabled.");

        var source = new DirectoryTypeSource(Path.GetFullPath(libraryDirectory), ignoredLibraryFileNames ?? Array.Empty<string>(), registration);
        registration.Register(mode, typeNames, assemblyNames, source, "Desktop");
        return registration;
    }

    /// <summary>
    /// Removes accelerators this registration owns once no other PowerForge module still owns them, then
    /// hands each freed name to the most recently imported module that still wants it.
    /// </summary>
    public void Unregister()
    {
        var owners = AcceleratorOwners();
        lock (owners.SyncRoot)
        {
            Live.Remove(this);
            foreach (var owned in _registered.ToArray())
            {
                if (owners[owned.Key] is not Hashtable entry || !ReferenceEquals(entry["Type"], owned.Value)) continue;
                (entry["Owners"] as Hashtable)?.Remove(_ownerId);
                if ((entry["Owners"] as Hashtable)?.Count > 0) continue;

                if (_table.TryGet(owned.Key, out var current) && ReferenceEquals(current, owned.Value))
                    _table.Remove(owned.Key);
                owners.Remove(owned.Key);
                if (_table.TryGet(owned.Key, out _)) continue;

                for (var i = Live.Count - 1; i >= 0; i--)
                {
                    if (!Live[i]._claims.TryGetValue(owned.Key, out var successor)) continue;
                    Live[i].Claim(owned.Key, successor, owners);
                    break;
                }
            }
            _registered.Clear();
            _claims.Clear();
        }
    }

    private static TypeAcceleratorRegistration Create(out AcceleratorTable? table, string moduleName, string? moduleRoot)
    {
        table = AcceleratorTable.TryCreate();
        var registration = new TypeAcceleratorRegistration(table ?? AcceleratorTable.Unavailable)
        {
            _module = ModuleDescription.Describe(moduleName ?? string.Empty, moduleRoot)
        };
        if (table is null)
            registration._warnings.Add("PowerShell type accelerator APIs are not available. Dependency type exposure is disabled.");
        return registration;
    }

    private TypeAcceleratorRegistration Warn(string message)
    {
        _warnings.Add(message);
        return this;
    }

    private void Register(string mode, string[]? typeNames, string[]? assemblyNames, ITypeSource source, string label)
    {
        var types = Normalize(typeNames);
        var assemblies = Normalize(assemblyNames);
        var byAssembly = string.Equals(mode, "Assembly", StringComparison.OrdinalIgnoreCase);
        var enumsOnly = string.Equals(mode, "Enums", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(mode, "AllowList", StringComparison.OrdinalIgnoreCase) && types.Length == 0)
        {
            Warn($"AllowList type accelerator mode was configured without type names. No {label} dependency type accelerators will be registered.");
            return;
        }

        if ((byAssembly || enumsOnly) && assemblies.Length == 0)
        {
            if (types.Length == 0)
            {
                Warn($"{mode} type accelerator mode was configured without assembly names or type names. No {label} dependency type accelerators will be registered.");
                return;
            }

            Warn($"{mode} type accelerator mode was configured without assembly names. Only explicitly configured type names will be registered.");
        }

        if (byAssembly || enumsOnly)
        {
            foreach (var assemblyName in assemblies)
            {
                var assembly = source.ImportAssembly(assemblyName);
                if (assembly is null)
                {
                    Warn(source.MissingAssemblyMessage(assemblyName));
                    continue;
                }

                Type[] exported;
                try { exported = assembly.GetExportedTypes(); }
                catch (Exception ex)
                {
                    Warn($"Could not enumerate exported types from {source.AssemblyLabel} '{assemblyName}' for type accelerator exposure: {ex.Message}");
                    continue;
                }

                foreach (var type in exported)
                {
                    if (enumsOnly && !type.IsEnum) continue;
                    Add(type, source);
                }
            }
        }

        foreach (var typeName in types)
        {
            var type = source.FindType(typeName);
            if (type is null)
            {
                Warn(source.MissingTypeMessage(typeName));
                continue;
            }

            Add(type, source);
        }

        lock (AcceleratorOwners().SyncRoot)
            Live.Add(this);

        if (_keptByOthers.Count > 0)
            Warn($"{_module}: {_keptByOthers.Count} type accelerator(s) already point to types another module loaded ({Sample(_keptByOthers)}); " +
                 "they keep doing so and switch to this module's types when that module is removed. Run Import-Module -Verbose for details.");
    }

    private static string Sample(List<string> names)
        => string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", ..." : string.Empty);

    private void Add(Type type, ITypeSource source)
    {
        var name = type.FullName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var owners = AcceleratorOwners();
        lock (owners.SyncRoot)
        {
            _claims[name!] = type;
            if (_table.TryGet(name!, out var existing) && !ReferenceEquals(existing, type))
            {
                var message = source.DescribeConflict(name!, existing!, type);
                _verbose.Add(message.Text);
                if (!message.IsVerbose) _keptByOthers.Add(name!);
                return;
            }

            Claim(name!, type, owners);
        }
    }

    /// <summary>Points the accelerator at <paramref name="type"/> (unless it already does) and records this owner.</summary>
    private void Claim(string name, Type type, Hashtable owners)
    {
        if (!_table.TryGet(name, out _))
        {
            try { _table.Add(name, type); }
            catch (Exception ex)
            {
                Warn($"Type accelerator '{name}' could not be registered from {type.Assembly.GetName().Name}: {ex.Message}");
                return;
            }

            if (owners[name] is not Hashtable previous || !ReferenceEquals(previous["Type"], type))
                owners[name] = NewOwnerEntry(type);
        }

        if (owners[name] is Hashtable entry && ReferenceEquals(entry["Type"], type) && entry["Owners"] is Hashtable entryOwners)
        {
            entryOwners[_ownerId] = true;
            _registered[name] = type;
        }
    }

    private static Hashtable NewOwnerEntry(Type type)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Type"] = type,
            ["Owners"] = new Hashtable(StringComparer.OrdinalIgnoreCase)
        };

    private static Hashtable AcceleratorOwners()
    {
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            if (domain.GetData(OwnersKey) is Hashtable existing) return existing;
            var created = Hashtable.Synchronized(new Hashtable(StringComparer.OrdinalIgnoreCase));
            domain.SetData(OwnersKey, created);
            return created;
        }
    }

    private static string[] Normalize(string[]? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private readonly struct ConflictMessage
    {
        public ConflictMessage(string text, bool isVerbose)
        {
            Text = text;
            IsVerbose = isVerbose;
        }

        public string Text { get; }
        public bool IsVerbose { get; }
    }

    private interface ITypeSource
    {
        string AssemblyLabel { get; }
        Assembly? ImportAssembly(string assemblyName);
        Type? FindType(string typeName);
        string MissingAssemblyMessage(string assemblyName);
        string MissingTypeMessage(string typeName);
        ConflictMessage DescribeConflict(string name, Type existing, Type candidate);
    }

#if NETCOREAPP
    private sealed class LoadContextTypeSource : ITypeSource
    {
        private readonly AssemblyLoadContext _context;
        private readonly string _libraryDirectory;
        private readonly TypeAcceleratorRegistration _owner;

        public LoadContextTypeSource(AssemblyLoadContext context, string libraryDirectory, TypeAcceleratorRegistration owner)
        {
            _context = context;
            _libraryDirectory = libraryDirectory;
            _owner = owner;
        }

        public string AssemblyLabel => "assembly";

        public Assembly? ImportAssembly(string assemblyName)
        {
            foreach (var assembly in _context.Assemblies)
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) return assembly;

            try
            {
                return _context.LoadFromAssemblyName(new AssemblyName(assemblyName));
            }
            catch
            {
                var path = Path.Combine(_libraryDirectory, assemblyName + ".dll");
                if (!File.Exists(path)) return null;
                try
                {
                    return _context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(path));
                }
                catch (Exception ex)
                {
                    _owner.Warn($"Could not load ALC assembly '{assemblyName}' for type accelerator exposure: {ex.Message}");
                    return null;
                }
            }
        }

        public Type? FindType(string typeName)
        {
            foreach (var assembly in _context.Assemblies)
            {
                var type = assembly.GetType(typeName, false, false);
                if (type is not null) return type;
            }

            if (!Directory.Exists(_libraryDirectory)) return null;
            foreach (var file in Directory.EnumerateFiles(_libraryDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var assembly = ImportAssembly(AssemblyName.GetAssemblyName(file).Name!);
                    var type = assembly?.GetType(typeName, false, false);
                    if (type is not null) return type;
                }
                catch
                {
                    // Native or unreadable files in the payload folder are not type sources.
                }
            }

            return null;
        }

        public string MissingAssemblyMessage(string assemblyName)
            => $"Assembly '{assemblyName}' was not found in the module AssemblyLoadContext. No type accelerators were registered for it.";

        public string MissingTypeMessage(string typeName)
            => $"Type '{typeName}' was not found in the module AssemblyLoadContext. No type accelerator was registered.";

        public ConflictMessage DescribeConflict(string name, Type existing, Type candidate)
        {
            var existingName = existing.Assembly.GetName();
            var candidateName = candidate.Assembly.GetName();
            if (ReferenceEquals(AssemblyLoadContext.GetLoadContext(existing.Assembly), AssemblyLoadContext.GetLoadContext(candidate.Assembly)) &&
                string.Equals(existingName.FullName, candidateName.FullName, StringComparison.Ordinal))
            {
                return new ConflictMessage(
                    $"Type accelerator '{name}' already exists in the same AssemblyLoadContext from the same assembly identity. Keeping the existing accelerator and skipping the duplicate type from {candidateName.Name}.",
                    isVerbose: true);
            }

            return new ConflictMessage(
                $"Type accelerator '{name}' already exists from {existingName.FullName}. Keeping the existing accelerator and skipping the ALC type from {candidateName.FullName}.",
                isVerbose: false);
        }
    }
#endif

    private sealed class DirectoryTypeSource : ITypeSource
    {
        private readonly string _libraryDirectory;
        private readonly string _libraryPrefix;
        private readonly HashSet<string> _ignored;
        private readonly TypeAcceleratorRegistration _owner;
        private readonly Dictionary<string, Assembly> _resolved = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

        public DirectoryTypeSource(string libraryDirectory, string[] ignoredLibraryFileNames, TypeAcceleratorRegistration owner)
        {
            _libraryDirectory = libraryDirectory;
            _libraryPrefix = libraryDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _ignored = new HashSet<string>(ignoredLibraryFileNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
            _owner = owner;
        }

        public string AssemblyLabel => "Desktop assembly";

        public Assembly? ImportAssembly(string assemblyName)
        {
            var simpleName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(assemblyName)
                : assemblyName;
            var fileName = simpleName + ".dll";
            if (_ignored.Contains(fileName))
            {
                _failed.Add(simpleName);
                return null;
            }

            if (_resolved.TryGetValue(simpleName, out var cached)) return cached;
            if (_failed.Contains(simpleName)) return null;

            var path = Path.Combine(_libraryDirectory, fileName);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsModuleAssembly(assembly) || ContentMatches(assembly, path))
                    return _resolved[simpleName] = assembly;
            }

            if (File.Exists(path))
            {
                try
                {
                    var loaded = Assembly.LoadFrom(path);
                    if (IsModuleAssembly(loaded) || ContentMatches(loaded, path) || IsAcceptedReuse(loaded, path))
                        return _resolved[simpleName] = loaded;

                    _failed.Add(simpleName);
                    _owner.Warn($"Desktop assembly '{simpleName}' resolved outside the module library directory. Skipping type accelerator exposure to avoid registering the wrong assembly version.");
                    return null;
                }
                catch (Exception ex)
                {
                    _failed.Add(simpleName);
                    _owner.Warn($"Could not load Desktop assembly '{simpleName}' for type accelerator exposure: {ex.Message}");
                    return null;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    return _resolved[simpleName] = assembly;
            }

            _failed.Add(simpleName);
            return null;
        }

        public Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsIgnored(assembly) || !IsModuleAssembly(assembly)) continue;
                var type = assembly.GetType(typeName, false, false);
                if (type is not null) return type;
            }

            if (Directory.Exists(_libraryDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(_libraryDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    if (_ignored.Contains(Path.GetFileName(file))) continue;
                    var type = ImportAssembly(Path.GetFileNameWithoutExtension(file))?.GetType(typeName, false, false);
                    if (type is not null) return type;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsIgnored(assembly)) continue;
                // A module-owned assembly with this identity exists but could not be selected above. Do not
                // silently fall back to a globally loaded copy with a different location or version.
                if (File.Exists(Path.Combine(_libraryDirectory, assembly.GetName().Name + ".dll"))) continue;
                var type = assembly.GetType(typeName, false, false);
                if (type is not null) return type;
            }

            return null;
        }

        public string MissingAssemblyMessage(string assemblyName)
            => $"Assembly '{assemblyName}' was not found in the module library directory. No Desktop type accelerators were registered for it.";

        public string MissingTypeMessage(string typeName)
            => $"Type '{typeName}' was not found in the module AppDomain. No Desktop type accelerator was registered.";

        public ConflictMessage DescribeConflict(string name, Type existing, Type candidate)
            => new(
                $"Type accelerator '{name}' already exists from {existing.Assembly.GetName().FullName}. Keeping it and skipping the Desktop type from {candidate.Assembly.GetName().FullName}.",
                isVerbose: false);

        private bool IsIgnored(Assembly assembly) => _ignored.Contains(assembly.GetName().Name + ".dll");

        // Windows PowerShell binds an assembly without a strong name by simple name, so LoadFrom returns the copy another
        // module already loaded. The bootstrapper's DependencyConflictCheck stops the import unless that copy is
        // compatible; when the import got this far, it is the copy this module runs on and the right accelerator target.
        private bool IsAcceptedReuse(Assembly assembly, string expectedPath)
        {
            if (assembly.IsDynamic || assembly.GetName().GetPublicKeyToken() is { Length: > 0 }) return false;
            try
            {
                if (AssemblyName.GetAssemblyName(expectedPath).GetPublicKeyToken() is { Length: > 0 }) return false;
            }
            catch
            {
                return false;
            }

            _owner._verbose.Add($"Desktop assembly '{assembly.GetName().Name}' {assembly.GetName().Version} was already loaded by another module; its types are used for type accelerators.");
            return true;
        }

        private bool IsModuleAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic) return false;
            try
            {
                return !string.IsNullOrWhiteSpace(assembly.Location) &&
                       Path.GetFullPath(assembly.Location).StartsWith(_libraryPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContentMatches(Assembly assembly, string expectedPath)
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(expectedPath)) return false;
            try
            {
                if (!string.Equals(assembly.GetName().FullName, AssemblyName.GetAssemblyName(expectedPath).FullName, StringComparison.Ordinal))
                    return false;

                var loadedPath = Path.GetFullPath(assembly.Location);
                var expectedFullPath = Path.GetFullPath(expectedPath);
                if (string.Equals(loadedPath, expectedFullPath, StringComparison.OrdinalIgnoreCase)) return true;
                return string.Equals(Hash(loadedPath), Hash(expectedFullPath), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            return Convert.ToBase64String(algorithm.ComputeHash(stream));
        }
    }

    /// <summary>PowerShell's process-wide accelerator table (System.Management.Automation.TypeAccelerators).</summary>
    private sealed class AcceleratorTable
    {
        internal static readonly AcceleratorTable Unavailable = new(null, null, null);

        private readonly MethodInfo? _add;
        private readonly MethodInfo? _remove;
        private readonly PropertyInfo? _get;

        private AcceleratorTable(MethodInfo? add, MethodInfo? remove, PropertyInfo? get)
        {
            _add = add;
            _remove = remove;
            _get = get;
        }

        internal static AcceleratorTable? TryCreate()
        {
            var automation = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "System.Management.Automation", StringComparison.Ordinal));
            var accelerators = automation?.GetType("System.Management.Automation.TypeAccelerators", false);
            if (accelerators is null) return null;

            var add = accelerators.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(Type) }, null);
            var remove = accelerators.GetMethod("Remove", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            var get = accelerators.GetProperty("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return add is null || remove is null || get is null ? null : new AcceleratorTable(add, remove, get);
        }

        internal bool TryGet(string name, out Type? type)
        {
            type = null;
            if (_get?.GetValue(null) is not IDictionary<string, Type> catalog) return false;
            return catalog.TryGetValue(name, out type);
        }

        internal void Add(string name, Type type) => _add!.Invoke(null, new object[] { name, type });

        internal void Remove(string name) => _remove!.Invoke(null, new object[] { name });
    }
}
