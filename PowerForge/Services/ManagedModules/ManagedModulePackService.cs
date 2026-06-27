using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Creates NuGet packages for PowerShell modules without external tooling.
/// </summary>
public sealed class ManagedModulePackService
{
    /// <summary>
    /// Creates a package from a module folder.
    /// </summary>
    /// <param name="request">Pack request.</param>
    /// <returns>Pack result.</returns>
    public ManagedModulePackResult Pack(ManagedModulePackRequest request)
    {
        Validate(request);

        var modulePath = Path.GetFullPath(request.ModulePath.Trim().Trim('"'));
        var manifestPath = ResolveManifestPath(modulePath, request.ManifestPath);
        var manifestText = File.ReadAllText(manifestPath);
        var name = ResolveName(modulePath, manifestPath, request.Name, manifestText);
        var version = ResolveVersion(request.Version, manifestPath);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, $"{name}.{version}.nupkg");

        if (File.Exists(packagePath))
        {
            if (!request.Force)
                throw new IOException($"Package already exists: {packagePath}");

            File.Delete(packagePath);
        }

        var metadata = CreateNuspec(name, version, request, manifestPath, manifestText);
        var fileCount = 0;
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            AddTextEntry(archive, $"{name}.nuspec", metadata.ToString(SaveOptions.DisableFormatting));
            AddTextEntry(archive, "[Content_Types].xml", CreateContentTypes());
            AddTextEntry(archive, "_rels/.rels", CreateRelationships());

            foreach (var file in EnumerateModuleFiles(modulePath))
            {
                var relativePath = NormalizeRelativePath(modulePath, file);
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using var source = File.OpenRead(file);
                using var destination = entry.Open();
                source.CopyTo(destination);
                fileCount++;
            }
        }

        return new ManagedModulePackResult
        {
            Name = name,
            Version = version,
            ModulePath = modulePath,
            ManifestPath = manifestPath,
            PackagePath = packagePath,
            FileCount = fileCount,
            PackageBytes = new FileInfo(packagePath).Length
        };
    }

    private static void Validate(ManagedModulePackRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModulePath))
            throw new ArgumentException("ModulePath is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new ArgumentException("OutputDirectory is required.", nameof(request));
        if (!Directory.Exists(request.ModulePath))
            throw new DirectoryNotFoundException($"Module folder was not found: {request.ModulePath}");
    }

    private static string ResolveManifestPath(string modulePath, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var resolved = Path.GetFullPath(manifestPath!.Trim().Trim('"'));
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"Module manifest was not found: {resolved}", resolved);

            return resolved;
        }

        var expected = Path.Combine(modulePath, Path.GetFileName(modulePath) + ".psd1");
        if (File.Exists(expected))
            return expected;

        var manifests = Directory.EnumerateFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (manifests.Length == 1)
            return manifests[0];
        if (manifests.Length == 0)
            throw new FileNotFoundException($"No module manifest was found in '{modulePath}'.");

        throw new InvalidOperationException($"Multiple module manifests were found in '{modulePath}'. Specify ManifestPath.");
    }

    private static string ResolveName(string modulePath, string manifestPath, string? requestedName, string manifestText)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
            return requestedName!.Trim();

        if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, "RootModule", out var rootModule) &&
            !string.IsNullOrWhiteSpace(rootModule))
            return Path.GetFileNameWithoutExtension(rootModule);

        var manifestName = Path.GetFileNameWithoutExtension(manifestPath);
        return string.IsNullOrWhiteSpace(manifestName) ? Path.GetFileName(modulePath) : manifestName;
    }

    private static string ResolveVersion(string? requestedVersion, string manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
            return requestedVersion!.Trim();

        var version = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Module manifest does not declare ModuleVersion.");

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(prerelease))
            return version + "-" + prerelease;

        return version!;
    }

    private static XDocument CreateNuspec(string name, string version, ManagedModulePackRequest request, string manifestPath, string manifestText)
    {
        var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd");
        var authors = FirstNonEmpty(request.Authors, ReadManifestString(manifestText, "Author"), ReadManifestString(manifestText, "CompanyName"), "Unknown");
        var description = FirstNonEmpty(request.Description, ReadManifestString(manifestText, "Description"), $"{name} PowerShell module.");
        var tags = request.Tags is { Count: > 0 }
            ? request.Tags
            : ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Tags");

        var metadata = new XElement(ns + "metadata",
            new XElement(ns + "id", name),
            new XElement(ns + "version", version),
            new XElement(ns + "authors", authors),
            new XElement(ns + "description", description));

        if (!string.IsNullOrWhiteSpace(request.ProjectUrl))
            metadata.Add(new XElement(ns + "projectUrl", request.ProjectUrl));
        if (tags.Count > 0)
            metadata.Add(new XElement(ns + "tags", string.Join(" ", tags)));

        return new XDocument(new XElement(ns + "package", metadata));
    }

    private static string? ReadManifestString(string manifestText, string key)
        => ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, key, out var value) ? value : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IEnumerable<string> EnumerateModuleFiles(string modulePath)
        => Directory.EnumerateFiles(modulePath, "*", SearchOption.AllDirectories)
            .Where(static file => !IsIgnoredPath(file))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase);

    private static bool IsIgnoredPath(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(static part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string modulePath, string file)
    {
        var root = modulePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relative = Path.GetFullPath(file).Substring(root.Length);
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"Module file '{file}' is outside the module root.");

        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static void AddTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string CreateContentTypes()
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
           "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
           "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
           "<Default Extension=\"psd1\" ContentType=\"application/octet\" />" +
           "<Default Extension=\"psm1\" ContentType=\"application/octet\" />" +
           "<Default Extension=\"ps1\" ContentType=\"application/octet\" />" +
           "<Default Extension=\"dll\" ContentType=\"application/octet\" />" +
           "<Default Extension=\"xml\" ContentType=\"application/xml\" />" +
           "</Types>";

    private static string CreateRelationships()
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
           "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />";
}
