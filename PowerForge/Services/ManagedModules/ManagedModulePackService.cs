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
        ValidateManifestMetadata(manifestPath, manifestText, request);
        var name = ManagedModulePackageIdentity.RequireSafeId(
            ResolveName(modulePath, manifestPath, request.Name, manifestText),
            nameof(request));
        var version = ManagedModulePackageIdentity.RequireSafeVersion(
            ResolveVersion(request.Version, manifestPath),
            nameof(request));
        var outputDirectory = Path.GetFullPath(request.OutputDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, $"{name}.{version}.nupkg");
        var manifestReferences = ResolveManifestFileReferences(manifestPath);
        var moduleFiles = EnumerateModuleFiles(modulePath, outputDirectory, packagePath, manifestReferences).ToArray();

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
            AddTextEntry(archive, "[Content_Types].xml", CreateContentTypes(moduleFiles));
            var corePropertiesPath = $"package/services/metadata/core-properties/{name}.{version}.psmdcp";
            AddTextEntry(archive, "_rels/.rels", CreateRelationships(name, corePropertiesPath));
            AddTextEntry(archive, corePropertiesPath, CreateCoreProperties(name, version, request, manifestPath, manifestText).ToString(SaveOptions.DisableFormatting));

            foreach (var file in moduleFiles)
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
            if (!IsSameDirectory(resolved, modulePath) && !IsUnderDirectory(resolved, modulePath))
                throw new InvalidOperationException($"Module manifest '{resolved}' must be inside module folder '{modulePath}'.");

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
        var manifestName = Path.GetFileNameWithoutExtension(manifestPath);
        var resolvedManifestName = string.IsNullOrWhiteSpace(manifestName) ? Path.GetFileName(modulePath) : manifestName;
        if (string.IsNullOrWhiteSpace(requestedName))
            return resolvedManifestName;

        var trimmed = ManagedModulePackageIdentity.RequireSafeId(requestedName!, nameof(requestedName));
        if (!trimmed.Equals(resolvedManifestName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Requested package id '{trimmed}' does not match module manifest id '{resolvedManifestName}'.");
        }

        return trimmed;
    }

    private static string ResolveVersion(string? requestedVersion, string manifestPath)
    {
        var version = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Module manifest does not declare ModuleVersion.");

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(prerelease))
            version += "-" + prerelease;

        if (!string.IsNullOrWhiteSpace(requestedVersion) &&
            ManagedModuleVersionComparer.Instance.Compare(requestedVersion!.Trim(), version) != 0)
        {
            throw new InvalidOperationException(
                $"Requested package version '{requestedVersion.Trim()}' does not match module manifest version '{version}'. Update the module manifest before packing.");
        }

        return version!;
    }

    private static XDocument CreateNuspec(string name, string version, ManagedModulePackRequest request, string manifestPath, string manifestText)
    {
        var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd");
        var authors = FirstNonEmpty(request.Authors, ReadManifestString(manifestText, "Author"), ReadManifestString(manifestText, "CompanyName"), "Unknown");
        var description = FirstNonEmpty(request.Description, ReadManifestString(manifestText, "Description"), $"{name} PowerShell module.");
        var sourceTags = request.Tags is { Count: > 0 }
            ? request.Tags
            : ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Tags");
        var tags = NormalizePackageTags(sourceTags);
        var copyright = FirstNonEmpty(
            ReadManifestString(manifestText, "Copyright"),
            authors.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : $"(c) {authors}. All rights reserved.");

        var metadata = new XElement(ns + "metadata",
            new XElement(ns + "id", name),
            new XElement(ns + "version", version),
            new XElement(ns + "authors", authors),
            new XElement(ns + "owners", authors),
            new XElement(ns + "requireLicenseAcceptance", ReadManifestLicenseAcceptance(manifestPath).ToString().ToLowerInvariant()),
            new XElement(ns + "description", description));

        if (!string.IsNullOrWhiteSpace(request.ProjectUrl))
            metadata.Add(new XElement(ns + "projectUrl", request.ProjectUrl));
        if (!string.IsNullOrWhiteSpace(copyright))
            metadata.Add(new XElement(ns + "copyright", copyright));
        if (tags.Count > 0)
            metadata.Add(new XElement(ns + "tags", string.Join(" ", tags)));
        var dependencies = ReadPackageDependencies(manifestPath);
        if (dependencies.Length > 0)
        {
            metadata.Add(new XElement(ns + "dependencies",
                dependencies.Select(dependency => CreateDependencyElement(ns, dependency))));
        }

        return new XDocument(new XElement(ns + "package", metadata));
    }

    private static void ValidateManifestMetadata(
        string manifestPath,
        string manifestText,
        ManagedModulePackRequest request)
    {
        if (request.SkipModuleManifestValidate)
            return;

        if (string.IsNullOrWhiteSpace(ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion")))
            throw new InvalidOperationException("Module manifest does not declare ModuleVersion.");
        if (string.IsNullOrWhiteSpace(request.Description) &&
            string.IsNullOrWhiteSpace(ReadManifestString(manifestText, "Description")))
        {
            throw new InvalidOperationException("Module manifest does not declare Description. Use SkipModuleManifestValidate to bypass this check.");
        }

        var author = FirstNonEmpty(ReadManifestString(manifestText, "Author"), ReadManifestString(manifestText, "CompanyName"));
        if (string.IsNullOrWhiteSpace(request.Authors) && string.IsNullOrWhiteSpace(author))
            throw new InvalidOperationException("Module manifest does not declare Author or CompanyName. Use SkipModuleManifestValidate to bypass this check.");
    }

    private static ManagedModuleDependencyInfo[] ReadPackageDependencies(string manifestPath)
    {
        var externalDependencies = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "ExternalModuleDependencies")
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(static dependency => dependency.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ModuleManifestValueReader.ReadRequiredModules(manifestPath)
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Where(module => !externalDependencies.Contains(module.ModuleName!))
            .Select(static module => new ManagedModuleDependencyInfo
            {
                Id = module.ModuleName,
                VersionRange = ToVersionRange(module)
            })
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static XElement CreateDependencyElement(XNamespace ns, ManagedModuleDependencyInfo dependency)
    {
        var element = new XElement(ns + "dependency", new XAttribute("id", dependency.Id));
        if (!string.IsNullOrWhiteSpace(dependency.VersionRange))
            element.Add(new XAttribute("version", dependency.VersionRange));

        return element;
    }

    private static string? ToVersionRange(RequiredModuleReference module)
    {
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            return "[" + module.RequiredVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion) && !string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "[" + module.ModuleVersion!.Trim() + "," + module.MaximumVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            return module.ModuleVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "(," + module.MaximumVersion!.Trim() + "]";

        return null;
    }

    private static string? ReadManifestString(string manifestText, string key)
        => ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, key, out var value) ? value : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyList<string> NormalizePackageTags(IReadOnlyList<string> tags)
    {
        var normalized = new List<string> { "PSModule" };
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;
            if (normalized.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
                continue;

            normalized.Add(tag.Trim());
        }

        return normalized;
    }

    private static bool ReadManifestLicenseAcceptance(string manifestPath)
        => ModuleManifestValueReader.ReadPsDataBoolean(manifestPath, "RequireLicenseAcceptance");

    private static ISet<string> ResolveManifestFileReferences(string manifestPath)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddReference(references, ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule"));
        AddReferences(references, ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "NestedModules"));
        AddReferences(references, ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "RequiredAssemblies"));
        AddReferences(references, ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "FileList"));
        return references;
    }

    private static void AddReferences(HashSet<string> references, IEnumerable<string> values)
    {
        foreach (var value in values)
            AddReference(references, value);
    }

    private static void AddReference(HashSet<string> references, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value!.Trim().Trim('"', '\'')
            .Replace('\\', '/')
            .TrimStart('.', '/');
        if (!string.IsNullOrWhiteSpace(normalized))
            references.Add(normalized);
    }

    private static IEnumerable<string> EnumerateModuleFiles(
        string modulePath,
        string outputDirectory,
        string packagePath,
        ISet<string> manifestReferences)
        => Directory.EnumerateFiles(modulePath, "*", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredPath(modulePath, file, outputDirectory, packagePath, manifestReferences))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase);

    private static bool IsIgnoredPath(
        string modulePath,
        string file,
        string outputDirectory,
        string packagePath,
        ISet<string> manifestReferences)
    {
        var fullPath = Path.GetFullPath(file);
        if (fullPath.Equals(Path.GetFullPath(packagePath), StringComparison.OrdinalIgnoreCase))
            return true;
        var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        if (IsSameDirectory(normalizedOutputDirectory, modulePath))
        {
            if (fullPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        else if (IsUnderDirectory(normalizedOutputDirectory, modulePath) &&
                 IsUnderDirectory(fullPath, normalizedOutputDirectory))
        {
            return true;
        }

        var relativePath = NormalizeRelativePath(modulePath, fullPath);
        var parts = relativePath.Split('/');
        if (parts.Any(static part =>
                part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".powerforge", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (parts.Any(static part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)) &&
            !IsManifestReferenced(relativePath, manifestReferences) &&
            !IsUnderManifestReferencedDirectory(relativePath, manifestReferences))
        {
            return true;
        }

        return false;
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(fullPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameDirectory(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsManifestReferenced(string relativePath, ISet<string> manifestReferences)
    {
        foreach (var reference in manifestReferences)
        {
            if (relativePath.Equals(reference, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(reference.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderManifestReferencedDirectory(string relativePath, ISet<string> manifestReferences)
    {
        foreach (var reference in manifestReferences)
        {
            var normalized = reference.TrimEnd('/');
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash <= 0)
                continue;

            var directory = normalized.Substring(0, lastSlash + 1);
            if (relativePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    private static string CreateContentTypes(IEnumerable<string> moduleFiles)
    {
        var defaults = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rels"] = "application/vnd.openxmlformats-package.relationships+xml",
            ["psmdcp"] = "application/vnd.openxmlformats-package.core-properties+xml",
            ["nuspec"] = "application/octet"
        };

        foreach (var file in moduleFiles)
        {
            var extension = Path.GetExtension(file).TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
                continue;

            if (!defaults.ContainsKey(extension))
                defaults.Add(extension, ResolveContentType(extension));
        }

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var document = new XDocument(
            new XElement(ns + "Types",
                defaults.Select(item => new XElement(ns + "Default",
                    new XAttribute("Extension", item.Key),
                    new XAttribute("ContentType", item.Value)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string ResolveContentType(string extension)
        => extension.Equals("xml", StringComparison.OrdinalIgnoreCase)
            ? "application/xml"
            : "application/octet";

    private static string CreateRelationships(string packageId, string corePropertiesPath)
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
           "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
           $"<Relationship Type=\"http://schemas.microsoft.com/packaging/2010/07/manifest\" Target=\"/{packageId}.nuspec\" Id=\"RManifest\" />" +
           $"<Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"/{corePropertiesPath}\" Id=\"RCoreProperties\" />" +
           "</Relationships>";

    private static XDocument CreateCoreProperties(
        string name,
        string version,
        ManagedModulePackRequest request,
        string manifestPath,
        string manifestText)
    {
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var authors = FirstNonEmpty(request.Authors, ReadManifestString(manifestText, "Author"), ReadManifestString(manifestText, "CompanyName"), "Unknown");
        var description = FirstNonEmpty(request.Description, ReadManifestString(manifestText, "Description"), $"{name} PowerShell module.");
        var sourceTags = request.Tags is { Count: > 0 }
            ? request.Tags
            : ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Tags");
        var tags = NormalizePackageTags(sourceTags);

        return new XDocument(
            new XElement(ns + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName),
                new XElement(dc + "creator", authors),
                new XElement(dc + "description", description),
                new XElement(dc + "identifier", name),
                new XElement(ns + "version", version),
                new XElement(ns + "keywords", string.Join(" ", tags))));
    }
}
