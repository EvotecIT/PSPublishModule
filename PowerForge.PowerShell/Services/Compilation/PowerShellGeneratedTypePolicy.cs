using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Limits typed compilation to CLR types supplied by the generated project's target framework.
/// </summary>
internal static class PowerShellGeneratedTypePolicy
{
    private static readonly string[] Net472ImplicitReferenceAssemblies =
    {
        "mscorlib.dll",
        "System.Core.dll",
        "System.Data.dll",
        "System.dll",
        "System.Drawing.dll",
        "System.IO.Compression.FileSystem.dll",
        "System.Numerics.dll",
        "System.Runtime.Serialization.dll",
        "System.Xml.dll",
        "System.Xml.Linq.dll"
    };
    private static readonly ConcurrentDictionary<string, Lazy<HashSet<string>>> TargetTypes = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsSupported(Type type, string? targetFramework = null)
    {
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsSupported(type.GetElementType()!, targetFramework);
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition != typeof(Dictionary<,>) &&
                definition != typeof(List<>) &&
                definition != typeof(Nullable<>))
                return false;
            return type.GetGenericArguments().All(argument => IsSupported(argument, targetFramework)) &&
                   IsSupportedNonGeneric(definition, targetFramework);
        }
        if (type.IsByRef || type.IsPointer)
            return false;
        return IsSupportedNonGeneric(type, targetFramework);
    }

    private static bool IsSupportedNonGeneric(Type type, string? targetFramework)
    {
        var location = type.Assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
            return type.Assembly == typeof(object).Assembly;
        var runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!PowerShellCompilationPathSafety.PathStartsWith(
                Path.GetFullPath(location),
                runtimeDirectory))
            return false;
        if (string.IsNullOrWhiteSpace(targetFramework))
            return true;

        var fullName = type.FullName;
        return !string.IsNullOrWhiteSpace(fullName) && GetTargetTypes(targetFramework!).Contains(fullName!);
    }

    private static HashSet<string> GetTargetTypes(string targetFramework)
        => TargetTypes.GetOrAdd(
            targetFramework,
            static framework => new Lazy<HashSet<string>>(
                () => ReadTargetTypes(framework),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static HashSet<string> ReadTargetTypes(string targetFramework)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in GetReferenceAssemblyPaths(targetFramework))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var name = GetTypeDefinitionName(reader, handle);
                    if (!string.IsNullOrWhiteSpace(name) && !name.EndsWith(".<Module>", StringComparison.Ordinal))
                        types.Add(name);
                }
                foreach (var handle in reader.ExportedTypes)
                {
                    var name = GetExportedTypeName(reader, handle);
                    if (!string.IsNullOrWhiteSpace(name))
                        types.Add(name);
                }
            }
            catch (BadImageFormatException)
            {
                // Native or otherwise non-managed reference assets cannot contribute CLR types.
            }
        }
        return types;
    }

    internal static string[] GetReferenceAssemblyPaths(string targetFramework)
    {
        var referenceDirectory = ResolveReferenceDirectory(targetFramework)
            ?? throw new InvalidOperationException($"Reference assemblies for target framework '{targetFramework}' could not be located.");
        return (targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
                ? Net472ImplicitReferenceAssemblies.Select(name => Path.Combine(referenceDirectory, name)).Where(File.Exists)
                : Directory.EnumerateFiles(referenceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string[] GetTargetRuntimeAssemblyPaths(string targetFramework)
    {
        var referenceDirectory = ResolveReferenceDirectory(targetFramework)
            ?? throw new InvalidOperationException($"Reference assemblies for target framework '{targetFramework}' could not be located.");
        return Directory.EnumerateFiles(
                referenceDirectory,
                "*.dll",
                targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveReferenceDirectory(string targetFramework)
    {
        if (targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase))
            return ResolveNet472ReferenceDirectory();
        if (!targetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) &&
            !targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            return null;

        var dotnetRoot = ResolveDotNetRoot();
        if (dotnetRoot is null)
            return null;
        var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packRoot))
            return null;
        var targetVersion = targetFramework.Substring(3);
        var targetMajor = Version.Parse(targetVersion).Major;
        return Directory.EnumerateDirectories(packRoot)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .Where(item => item.Version is not null && item.Version.Major == targetMajor)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(item.Path, "ref", targetFramework))
            .FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveNet472ReferenceDirectory()
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var root = Path.Combine(packageRoot!, "microsoft.netframework.referenceassemblies.net472");
        if (Directory.Exists(root))
        {
            var fromPackage = Directory.EnumerateDirectories(root)
                .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
                .Where(item => item.Version is not null)
                .OrderByDescending(item => item.Version)
                .Select(item => Path.Combine(item.Path, "build", ".NETFramework", "v4.7.2"))
                .FirstOrDefault(Directory.Exists);
            if (fromPackage is not null)
                return fromPackage;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var installed = Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.7.2");
            if (Directory.Exists(installed))
                return installed;
        }
        return null;
    }

    private static string? ResolveDotNetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(static directory => !string.IsNullOrWhiteSpace(directory));
        return ResolveDotNetRoot(configured, pathDirectories);
    }

    internal static string? ResolveDotNetRoot(
        string? configured,
        IEnumerable<string> pathDirectories,
        Func<string, string?>? sdkListProbe = null)
    {
        var configuredRoot = NormalizeDotNetRoot(configured);
        if (configuredRoot is not null)
            return configuredRoot;
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        foreach (var directory in pathDirectories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executable);
                if (!File.Exists(candidate))
                    continue;
                var directRoot = NormalizeDotNetRoot(Path.GetDirectoryName(Path.GetFullPath(candidate)));
                if (directRoot is not null)
                    return directRoot;
#if NET8_0_OR_GREATER
                var target = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
                var linkedRoot = NormalizeDotNetRoot(target is null ? null : Path.GetDirectoryName(target.FullName));
                if (linkedRoot is not null)
                    return linkedRoot;
#endif
                var probedRoot = ResolveDotNetRootFromSdkList((sdkListProbe ?? ProbeDotNetSdkList)(candidate));
                if (probedRoot is not null)
                    return probedRoot;
            }
            catch
            {
                // Ignore malformed PATH entries and continue to the next candidate.
            }
        }
        return null;
    }

    private static string? ProbeDotNetSdkList(string executable)
    {
        var result = new ProcessRunner().RunAsync(new ProcessRunRequest(
                executable,
                Environment.CurrentDirectory,
                new[] { "--list-sdks" },
                TimeSpan.FromSeconds(10)))
            .GetAwaiter()
            .GetResult();
        return result.Succeeded ? result.StdOut : null;
    }

    private static string? ResolveDotNetRootFromSdkList(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        foreach (var line in Enumerable.Reverse(output!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)))
        {
            var closeBracket = line.LastIndexOf(']');
            var openBracket = closeBracket > 0 ? line.LastIndexOf('[', closeBracket) : -1;
            if (openBracket < 0 || closeBracket <= openBracket + 1)
                continue;
            var sdkDirectory = line.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
            var root = NormalizeDotNetRoot(Path.GetFullPath(Path.Combine(sdkDirectory, "..")));
            if (root is not null)
                return root;
        }
        return null;
    }

    private static string? NormalizeDotNetRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        var root = Path.GetFullPath(candidate);
        return Directory.Exists(Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref")) ? root : null;
    }

    private static Version? ParseVersion(string value)
        => Version.TryParse(value.Split('-')[0], out var version) ? version : null;

    internal static string GetTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
            return GetTypeDefinitionName(reader, declaring) + "+" + name;
        var @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    internal static string GetExportedTypeName(MetadataReader reader, ExportedTypeHandle handle)
    {
        var exported = reader.GetExportedType(handle);
        var name = reader.GetString(exported.Name);
        if (exported.Implementation.Kind == HandleKind.ExportedType)
            return GetExportedTypeName(reader, (ExportedTypeHandle)exported.Implementation) + "+" + name;
        var @namespace = reader.GetString(exported.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    internal static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetTypeReferenceName(reader, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        var @namespace = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }
}
