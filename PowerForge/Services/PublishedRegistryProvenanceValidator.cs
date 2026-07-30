using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

internal static class PublishedRegistryProvenanceValidator
{
    internal const string ModuleProvenanceFileName = "PowerForge.ReleaseProvenance.json";

    internal static void ValidateNuGetPackages(
        IEnumerable<string> packagePaths,
        string expectedVersion,
        string expectedRepositoryUrl,
        string expectedCommit)
    {
        foreach (var packagePath in packagePaths.Where(static path =>
                     string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase)))
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspec = archive.Entries.SingleOrDefault(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
                throw new InvalidOperationException(
                    $"Published package '{Path.GetFileName(packagePath)}' has no nuspec provenance.");

            using var stream = nuspec.Open();
            var document = XDocument.Load(stream);
            var metadata = document.Root?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));
            var version = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            var repository = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "repository", StringComparison.OrdinalIgnoreCase));
            Validate(
                Path.GetFileName(packagePath),
                version,
                repository?.Attribute("url")?.Value,
                repository?.Attribute("commit")?.Value,
                expectedVersion,
                expectedRepositoryUrl,
                expectedCommit);
        }
    }

    internal static void ValidateModuleArchives(
        IEnumerable<string> archivePaths,
        string expectedModuleName,
        string expectedVersion,
        string expectedRepositoryUrl,
        string expectedCommit)
    {
        foreach (var archivePath in archivePaths)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var matches = archive.Entries.Where(entry =>
                string.Equals(
                    Path.GetFileName(entry.FullName.Replace('\\', '/')),
                    ModuleProvenanceFileName,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Published module archive '{Path.GetFileName(archivePath)}' must contain exactly one {ModuleProvenanceFileName}.");

            using var stream = matches[0].Open();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var moduleName = root.TryGetProperty("moduleName", out var moduleNameElement)
                ? moduleNameElement.GetString()
                : null;
            if (!string.Equals(moduleName, expectedModuleName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Published module archive '{Path.GetFileName(archivePath)}' identifies module '{moduleName}', expected '{expectedModuleName}'.");
            Validate(
                Path.GetFileName(archivePath),
                root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null,
                root.TryGetProperty("repository", out var repositoryElement) ? repositoryElement.GetString() : null,
                root.TryGetProperty("commit", out var commitElement) ? commitElement.GetString() : null,
                expectedVersion,
                expectedRepositoryUrl,
                expectedCommit);
        }
    }

    private static void Validate(
        string artifactName,
        string? actualVersion,
        string? actualRepositoryUrl,
        string? actualCommit,
        string expectedVersion,
        string expectedRepositoryUrl,
        string expectedCommit)
    {
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Published registry artifact '{artifactName}' has version provenance '{actualVersion ?? "<missing>"}', expected '{expectedVersion}'.");
        if (!RepositoryUrlsEqual(actualRepositoryUrl, expectedRepositoryUrl))
            throw new InvalidOperationException(
                $"Published registry artifact '{artifactName}' has repository provenance '{actualRepositoryUrl ?? "<missing>"}', expected '{expectedRepositoryUrl}'.");
        if (!string.Equals(actualCommit, expectedCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Published registry artifact '{artifactName}' has commit provenance '{actualCommit ?? "<missing>"}', expected '{expectedCommit}'.");
    }

    private static bool RepositoryUrlsEqual(string? left, string? right)
        => string.Equals(
            NormalizeRepositoryUrl(left),
            NormalizeRepositoryUrl(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRepositoryUrl(string? value)
        => (value ?? string.Empty).Trim().TrimEnd('/');
}
