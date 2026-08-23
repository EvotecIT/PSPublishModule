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
    private static readonly ConcurrentDictionary<string, Lazy<HashSet<string>>> TargetTypes = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsSupported(Type type, string? targetFramework = null)
    {
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsSupported(type.GetElementType()!, targetFramework);
        if (type.IsGenericType || type.IsByRef || type.IsPointer)
            return false;
        var location = type.Assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
            return type.Assembly == typeof(object).Assembly;
        var runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(location).StartsWith(
                runtimeDirectory,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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
        var referenceDirectory = ResolveReferenceDirectory(targetFramework)
            ?? throw new InvalidOperationException($"Reference assemblies for target framework '{targetFramework}' could not be located.");
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(referenceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
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
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator)
                     .Where(static directory => !string.IsNullOrWhiteSpace(directory)))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executable);
                if (File.Exists(candidate))
                    return Path.GetDirectoryName(Path.GetFullPath(candidate));
            }
            catch
            {
                // Ignore malformed PATH entries and continue to the next candidate.
            }
        }
        return null;
    }

    private static Version? ParseVersion(string value)
        => Version.TryParse(value.Split('-')[0], out var version) ? version : null;

    private static string GetTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
            return GetTypeDefinitionName(reader, declaring) + "+" + name;
        var @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    private static string GetExportedTypeName(MetadataReader reader, ExportedTypeHandle handle)
    {
        var exported = reader.GetExportedType(handle);
        var name = reader.GetString(exported.Name);
        if (exported.Implementation.Kind == HandleKind.ExportedType)
            return GetExportedTypeName(reader, (ExportedTypeHandle)exported.Implementation) + "+" + name;
        var @namespace = reader.GetString(exported.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }
}
