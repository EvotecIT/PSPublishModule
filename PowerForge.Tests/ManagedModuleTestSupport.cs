using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForgeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

internal sealed class TestDependency
{
    public TestDependency(string id, string? version, string? targetFramework)
    {
        Id = id;
        Version = version;
        TargetFramework = targetFramework;
    }

    public string Id { get; }

    public string? Version { get; }

    public string? TargetFramework { get; }
}

internal static class TestPackageFactory
{
    public static void Create(
        string packagePath,
        string id,
        string version,
        IReadOnlyList<TestDependency>? dependencies = null,
        IReadOnlyDictionary<string, string>? files = null,
        bool requireLicenseAcceptance = false)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry(id + ".nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write(CreateNuspec(id, version, dependencies, requireLicenseAcceptance));
        }

        if (files is null)
            return;

        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Key);
            using var entryWriter = new StreamWriter(entry.Open());
            entryWriter.Write(file.Value);
        }
    }

    public static byte[] CreateBytes(string id, string version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nuspec = archive.CreateEntry(id + ".nuspec");
            using var writer = new StreamWriter(nuspec.Open());
            writer.Write(CreateNuspec(id, version));
        }

        return stream.ToArray();
    }

    public static string CreateNuspec(
        string id,
        string version,
        IReadOnlyList<TestDependency>? dependencies = null,
        bool requireLicenseAcceptance = false)
    {
        var licenseAcceptanceXml = requireLicenseAcceptance
            ? "    <requireLicenseAcceptance>true</requireLicenseAcceptance>" + Environment.NewLine
            : string.Empty;
        var dependencyXml = CreateDependencyXml(dependencies);
        return $"""
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{id}</id>
    <version>{version}</version>
    <authors>Evotec</authors>
    <description>Test package.</description>
    <projectUrl>https://example.test/{id}</projectUrl>
    <license type="expression">MIT</license>
{licenseAcceptanceXml}    <tags>powershell automation company</tags>
{dependencyXml}
  </metadata>
</package>
""";
    }

    private static string CreateDependencyXml(IReadOnlyList<TestDependency>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0)
            return string.Empty;

        var direct = dependencies
            .Where(static dependency => string.IsNullOrWhiteSpace(dependency.TargetFramework))
            .Select(CreateDependencyElement);
        var grouped = dependencies
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency.TargetFramework))
            .GroupBy(static dependency => dependency.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"      <group targetFramework=\"{group.Key}\">{Environment.NewLine}" +
                             string.Join(Environment.NewLine, group.Select(CreateDependencyElement)) +
                             $"{Environment.NewLine}      </group>");

        return "    <dependencies>" + Environment.NewLine +
               string.Join(Environment.NewLine, direct.Concat(grouped)) +
               Environment.NewLine +
               "    </dependencies>";
    }

    private static string CreateDependencyElement(TestDependency dependency)
        => string.IsNullOrWhiteSpace(dependency.Version)
            ? $"        <dependency id=\"{dependency.Id}\" />"
            : $"        <dependency id=\"{dependency.Id}\" version=\"{dependency.Version}\" />";
}

internal static class TestHash
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
