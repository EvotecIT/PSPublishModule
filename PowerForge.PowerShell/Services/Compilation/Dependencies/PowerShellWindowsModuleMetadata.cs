using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>Finds binary type metadata declared by Windows inbox and optional PowerShell modules.</summary>
internal static class PowerShellWindowsModuleMetadata
{
    internal const string AssemblyProvenance = "WindowsHostModuleAssembly";
    internal const string ManifestProvenance = "WindowsHostModuleManifest";
    private const string ModulePrefix = "windows-module/";
    private const string GacPrefix = "windows-gac/";

    internal static bool SupportsTarget(string? runtimeIdentifier)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
           (string.IsNullOrEmpty(runtimeIdentifier) ||
            runtimeIdentifier!.Equals("win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                StringComparison.OrdinalIgnoreCase));

    internal static IEnumerable<string> FindManifests(string moduleName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || moduleName.Length == 0 || !char.IsLetterOrDigit(moduleName[0]) ||
            moduleName.Any(static character => !char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-')))
            yield break;
        var root = Path.Combine(ModuleRoot, moduleName);
        if (!Directory.Exists(root)) yield break;
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(root, "Windows module metadata must not traverse links.");
        var directories = Directory.EnumerateDirectories(root)
            .Where(directory => Version.TryParse(Path.GetFileName(directory), out _)).Prepend(root);
        foreach (var directory in directories)
        {
            var path = Path.Combine(directory, moduleName + ".psd1");
            if (!File.Exists(path)) continue;
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Windows module metadata must not traverse links.");
            yield return path;
        }
    }

    internal static IEnumerable<string> FindDeclaredAssemblies(string manifestPath)
    {
        var root = Path.GetDirectoryName(manifestPath)!;
        string[] references;
        try
        {
            references = (ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath, "RequiredAssemblies") ??
                          Array.Empty<string>())
                .Concat(ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
                .Append(PowerShellCompiledModuleManifest.ReadRootModule(manifestPath) ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (InvalidOperationException)
        {
            // A computed declaration gives no static metadata contract.
            references = Array.Empty<string>();
        }
        foreach (var declaration in references)
        {
            if (string.IsNullOrWhiteSpace(declaration) || Path.IsPathRooted(declaration)) continue;
            var explicitAssembly = declaration.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var reference = explicitAssembly ? declaration : declaration + ".dll";
            var candidate = Path.GetFullPath(Path.Combine(root, reference));
            if (!IsUnder(candidate, root)) continue;
            var path = File.Exists(candidate) ? candidate : explicitAssembly ? FindGacAssembly(reference) : null;
            if (path is null) continue;
            RequireTrustedAssemblyPath(path);
            if (IsManagedHostAssembly(path)) yield return path;
        }
    }

    private static bool IsManagedHostAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return HasCompatibleArchitecture(pe) && pe.HasMetadata && pe.GetMetadataReader().IsAssembly;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    internal static bool HasCompatibleArchitecture(PEReader image)
        => image.PEHeaders.CorHeader is { } header &&
           HasCompatibleArchitecture(image.PEHeaders.CoffHeader.Machine, header.Flags, RuntimeInformation.ProcessArchitecture);

    internal static bool HasCompatibleArchitecture(Machine machine, CorFlags flags, Architecture architecture)
    {
        const CorFlags restrictedTo32Bit = CorFlags.Requires32Bit | CorFlags.Prefers32Bit;
        if (machine == Machine.I386 && (flags & (restrictedTo32Bit | CorFlags.ILOnly)) == CorFlags.ILOnly)
            return true;
        return machine switch
        {
            Machine.I386 => architecture == Architecture.X86,
            Machine.Amd64 => architecture == Architecture.X64 && (flags & restrictedTo32Bit) == 0,
            Machine.Arm or Machine.ArmThumb2 => architecture == Architecture.Arm,
            Machine.Arm64 => architecture == Architecture.Arm64 && (flags & restrictedTo32Bit) == 0,
            _ => false
        };
    }

    internal static void RequireTrustedAssemblyPath(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !(IsUnder(path, ModuleRoot) || IsUnder(path, GacRoot)))
            throw new InvalidOperationException("Windows module metadata escaped the host module and assembly roots.");
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Windows module metadata must not traverse links.");
    }

    internal static string DescribePath(string path)
    {
        RequireTrustedAssemblyPath(path);
        var modulePath = IsUnder(path, ModuleRoot);
        var root = modulePath ? ModuleRoot : GacRoot;
        return (modulePath ? ModulePrefix : GacPrefix) + FrameworkCompatibility.GetRelativePath(root, path).Replace('\\', '/');
    }

    internal static string ResolvePath(string source)
    {
        var modulePath = source.StartsWith(ModulePrefix, StringComparison.Ordinal);
        if (!modulePath && !source.StartsWith(GacPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The Windows host metadata identity is malformed.");
        var root = modulePath ? ModuleRoot : GacRoot;
        var relative = source.Substring((modulePath ? ModulePrefix : GacPrefix).Length).Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsUnder(path, root)) throw new InvalidOperationException("Windows host metadata escaped its declared root.");
        RequireTrustedAssemblyPath(path);
        return path;
    }

    private static string? FindGacAssembly(string reference)
    {
        if (!string.Equals(reference, Path.GetFileName(reference), StringComparison.Ordinal)) return null;
        var name = Path.GetFileNameWithoutExtension(reference);
        var candidates = new[] { Environment.Is64BitProcess ? "GAC_64" : "GAC_32", "GAC_MSIL" }
            .Select(architecture => Path.Combine(GacRoot, architecture, name))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateDirectories(directory, "v4.0_*", SearchOption.TopDirectoryOnly))
            .Select(directory => Path.Combine(directory, reference))
            .Where(File.Exists).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool IsUnder(string path, string root)
        => PowerShellCompilationPathSafety.PathStartsWith(path,
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);

    private static string ModuleRoot => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules");
    private static string GacRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "assembly");
}
