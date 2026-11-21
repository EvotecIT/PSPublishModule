using System.Text.Json;

namespace PowerForge;

public sealed partial class ManagedModuleRestoreService
{
    private static void RequireOutsideDestination(string path, string destination)
    {
        if (IsInside(path, destination) ||
            IsInside(FrameworkCompatibility.ResolvePhysicalPath(path), FrameworkCompatibility.ResolvePhysicalPath(destination)))
            throw new ArgumentException("Keep the requirements and lock file outside the module destination.");

        static bool IsInside(string path, string destination)
        {
            var boundary = destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(destination);
            return string.Equals(path, destination, comparison) || path.StartsWith(boundary, comparison);
        }
    }

    private static void ValidateModuleDestinations(string destination, IEnumerable<PreparedPackage> packages)
    {
        var physicalRoot = FrameworkCompatibility.ResolvePhysicalPath(destination);
        foreach (var package in packages)
        {
            var path = Path.Combine(physicalRoot, package.Pin.Name);
            if (!FrameworkCompatibility.PathComparer.Equals(path, FrameworkCompatibility.ResolvePhysicalPath(path)))
                throw new InvalidOperationException($"Restore cannot write module '{package.Pin.Name}' through a linked module directory.");
        }
    }

    private static ManagedModuleRestoreRequirement[] ReadRequirements(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Requirements must be a JSON object mapping module names to exact version strings.");

        var requirements = new Dictionary<string, ManagedModuleRestoreRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var name = ManagedModuleStandaloneInput.ValidateName(property.Name);
            var version = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            var requirement = new ManagedModuleRestoreRequirement(name, ManagedModuleStandaloneInput.ValidateVersion(name, version));
            if (requirements.ContainsKey(name))
                throw new InvalidOperationException($"Requirements list '{name}' more than once.");
            requirements.Add(name, requirement);
        }

        if (requirements.Count == 0)
            throw new InvalidOperationException("Requirements must list at least one module.");
        return requirements.Values.OrderBy(static requirement => requirement.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ManagedModuleRestoreLock ReadLock(
        string path, ManagedModuleRestoreRequirement[] requirements, string source)
    {
        var restoreLock = JsonSerializer.Deserialize(File.ReadAllText(path), ManagedModuleRestoreJsonContext.Default.ManagedModuleRestoreLock);
        if (restoreLock?.SchemaVersion != LockSchemaVersion ||
            restoreLock.Requirements is null || restoreLock.Packages is null)
            throw new InvalidOperationException("The restore lock format is invalid or uses an unsupported schema version.");
        if (!string.Equals(restoreLock.Source, source, StringComparison.Ordinal))
            throw new InvalidOperationException("The restore lock source differs from the requested source.");
        if (restoreLock.Requirements.Length != requirements.Length)
            throw new InvalidOperationException("The restore lock does not match the requirements. Use --write-lock to resolve again.");

        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in restoreLock.Requirements)
        {
            if (root is null)
                throw new InvalidOperationException("The restore lock contains an empty requirement.");
            var name = ManagedModuleStandaloneInput.ValidateName(root.Name);
            if (roots.ContainsKey(name))
                throw new InvalidOperationException($"The restore lock lists requirement '{name}' more than once.");
            roots.Add(name, ManagedModuleStandaloneInput.ValidateVersion(name, root.Version));
        }

        foreach (var requirement in requirements)
        {
            if (!roots.TryGetValue(requirement.Name, out var version) || !SameVersion(requirement.Version, version))
                throw new InvalidOperationException("The restore lock does not match the requirements. Use --write-lock to resolve again.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in restoreLock.Packages)
        {
            if (package is null)
                throw new InvalidOperationException("The restore lock contains an empty package.");
            var name = ManagedModuleStandaloneInput.ValidateName(package.Name);
            if (!names.Add(name))
                throw new InvalidOperationException($"The restore lock lists package '{name}' more than once.");
            ManagedModuleStandaloneInput.ValidateVersion(name, package.Version);
            if (ManagedModulePackageIntegrity.NormalizeSha256(package.Sha256) is null)
                throw new InvalidOperationException($"The restore lock is missing a SHA-256 for '{name}'.");
        }

        foreach (var requirement in requirements)
        {
            if (!restoreLock.Packages.Any(package => package.Name.Equals(requirement.Name, StringComparison.OrdinalIgnoreCase) &&
                                                   SameVersion(package.Version, requirement.Version)))
                throw new InvalidOperationException($"The restore lock is missing '{requirement.Name}' version '{requirement.Version}'.");
        }

        return restoreLock;
    }

    private static void WriteLock(string path, ManagedModuleRestoreLock restoreLock)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(restoreLock, ManagedModuleRestoreJsonContext.Default.ManagedModuleRestoreLock) + "\n");
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
