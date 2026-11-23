using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    internal static string PrepareExactNuGetClosureLock(
        PowerShellCompilationBuildSpec spec,
        string projectPath)
    {
        if (string.IsNullOrWhiteSpace(spec.NuGetLockFilePath)) return string.Empty;

        var sourcePath = Path.GetFullPath(spec.NuGetLockFilePath!.Trim().Trim('"'));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The exact NuGet closure lock was not found.", sourcePath);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
            sourcePath,
            "The exact NuGet closure lock traverses a symbolic link or junction.");

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("Generated project has no directory.");
        var workspaceLockPath = Path.Combine(projectDirectory, "packages.lock.json");
        if (!PowerShellCompilationPathSafety.PathEquals(sourcePath, workspaceLockPath))
            File.Copy(sourcePath, workspaceLockPath, overwrite: true);
        PowerShellCompilationPathSafety.EnsureNoLinks(
            projectDirectory,
            workspaceLockPath,
            "The generated-project NuGet closure lock traverses a symbolic link or junction.");
        if (!ComputeSha256(sourcePath).Equals(ComputeSha256(workspaceLockPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The generated-project NuGet closure lock differs from the reviewed lock.");

        AddMissingDirectPackageReferences(projectPath, workspaceLockPath);
        return ComputeSha256(workspaceLockPath);
    }

    private static void AddMissingDirectPackageReferences(string projectPath, string lockPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!document.RootElement.TryGetProperty("dependencies", out var dependencyGroups) ||
            dependencyGroups.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The exact NuGet closure lock has no dependency groups.");

        var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in dependencyGroups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var package in group.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("type", out var type) ||
                    !string.Equals(type.GetString(), "Direct", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!package.Value.TryGetProperty("resolved", out var resolved) || string.IsNullOrWhiteSpace(resolved.GetString()))
                    throw new InvalidDataException($"Direct NuGet closure entry '{package.Name}' has no exact version.");
                var version = resolved.GetString()!;
                if (direct.TryGetValue(package.Name, out var existing) && !existing.Equals(version, StringComparison.Ordinal))
                    throw new InvalidDataException($"The exact NuGet closure lock contains conflicting direct versions for '{package.Name}'.");
                direct[package.Name] = version;
            }
        }

        var project = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var root = project.Root ?? throw new InvalidDataException("Generated compilation project has no root element.");
        var existingReferences = root.Descendants()
            .Where(static element => element.Name.LocalName.Equals("PackageReference", StringComparison.Ordinal))
            .ToArray();
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var package in direct.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var existing = existingReferences.SingleOrDefault(reference =>
                string.Equals(
                    reference.Attribute("Include")?.Value ?? reference.Attribute("Update")?.Value,
                    package.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                missing.Add(package);
                continue;
            }
            var version = existing.Attribute("Version")?.Value ??
                          existing.Elements().SingleOrDefault(static element => element.Name.LocalName.Equals("Version", StringComparison.Ordinal))?.Value;
            if (!string.Equals(version, package.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Generated compilation project requests '{package.Key}/{version}', but the exact NuGet closure lock requires '{package.Key}/{package.Value}'.");
            }
        }

        if (missing.Count == 0) return;
        var itemGroup = new XElement(root.Name.Namespace + "ItemGroup",
            missing.Select(package => new XElement(
                root.Name.Namespace + "PackageReference",
                new XAttribute("Include", package.Key),
                new XAttribute("Version", package.Value),
                new XAttribute("PrivateAssets", "all"))));
        root.Add(itemGroup);
        var settings = new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = true,
            NewLineChars = Environment.NewLine
        };
        using var writer = XmlWriter.Create(projectPath, settings);
        project.Save(writer);
    }
}
