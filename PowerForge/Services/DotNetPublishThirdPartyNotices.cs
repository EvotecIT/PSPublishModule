using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Builds binary dependency notices from an exact .NET dependency graph and reviewed license texts.</summary>
public static class DotNetPublishThirdPartyNotices
{
    /// <summary>Validates runtime package coverage and writes notices and the exact package inventory.</summary>
    public static void Generate(string projectRoot, string dependencyFile, string manifestFile, string outputDirectory)
    {
        using var dependencies = JsonDocument.Parse(File.ReadAllText(dependencyFile));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestFile));
        if (manifest.RootElement.GetProperty("SchemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported third-party notice manifest schema.");
        var declarations = manifest.RootElement.GetProperty("Packages").EnumerateArray()
            .ToDictionary(item => item.GetProperty("Package").GetString()!, StringComparer.OrdinalIgnoreCase);
        var packages = dependencies.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(item => item.Value.GetProperty("type").GetString() is "package" or "runtimepack")
            .Select(item => item.Name.StartsWith("runtimepack.", StringComparison.Ordinal) ? item.Name.Substring(12) : item.Name)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var builder = new StringBuilder("Third-party software included in this application\n\n");
        foreach (string package in packages)
        {
            if (!declarations.TryGetValue(package, out var declaration))
                throw new InvalidOperationException($"No reviewed license notices cover runtime package '{package}'.");
            builder.Append(package).Append('\n').Append(declaration.GetProperty("Authors").GetString()).Append('\n');
            builder.Append("License: ").Append(declaration.GetProperty("License").GetString()).Append("\n\n");
            var files = declaration.GetProperty("Files").EnumerateArray().ToArray();
            if (files.Length == 0) throw new InvalidOperationException($"Runtime package '{package}' has no license text.");
            foreach (var file in files)
            {
                string relative = file.GetProperty("Path").GetString()!;
                string path = Path.GetFullPath(Path.Combine(projectRoot, relative));
                string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, FrameworkCompatibility.GetPathStringComparison(root)))
                    throw new InvalidOperationException("Notice texts must reside inside the project root.");
                byte[] bytes = File.ReadAllBytes(path);
                using var hash = SHA256.Create();
                string sha = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (!sha.Equals(file.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Reviewed license text changed: {relative}");
                builder.Append("Source: ").Append(file.GetProperty("Source").GetString()).Append('\n');
                builder.Append(Encoding.UTF8.GetString(bytes)).Append("\n\n");
            }
        }
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "THIRD_PARTY_NOTICES.txt"), builder.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "runtime-package-inventory.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, runtimeTarget = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString(), packages },
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}
