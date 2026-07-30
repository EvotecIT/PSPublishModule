using System.IO.Compression;
using System.Text;

namespace PowerForge.Tests;

public sealed class PublishedRegistryProvenanceValidatorTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Repository = "https://github.com/EvotecIT/PSPublishModule";

    [Fact]
    public void ValidateNuGetPackages_BindsRepositoryVersionAndCommit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".nupkg");
        try
        {
            CreateNuGetPackage(path, Commit);
            PublishedRegistryProvenanceValidator.ValidateNuGetPackages(
                [path],
                "3.0.81",
                Repository,
                Commit);

            CreateNuGetPackage(path, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
            var exception = Assert.Throws<InvalidOperationException>(() =>
                PublishedRegistryProvenanceValidator.ValidateNuGetPackages(
                    [path],
                    "3.0.81",
                    Repository,
                    Commit));
            Assert.Contains("commit provenance", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateModuleArchives_BindsRepositoryVersionModuleAndCommit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            CreateModuleArchive(path, Commit);
            PublishedRegistryProvenanceValidator.ValidateModuleArchives(
                [path],
                "PSPublishModule",
                "3.0.81",
                Repository,
                Commit);

            CreateModuleArchive(path, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
            var exception = Assert.Throws<InvalidOperationException>(() =>
                PublishedRegistryProvenanceValidator.ValidateModuleArchives(
                    [path],
                    "PSPublishModule",
                    "3.0.81",
                    Repository,
                    Commit));
            Assert.Contains("commit provenance", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateNuGetPackage(string path, string commit)
    {
        File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "PowerForge.nuspec",
            $"""
             <package><metadata>
               <id>PowerForge</id>
               <version>3.0.81</version>
               <repository type="git" url="{Repository}" commit="{commit}" />
             </metadata></package>
             """);
    }

    private static void CreateModuleArchive(string path, string commit)
    {
        File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            $"PSPublishModule/{PublishedRegistryProvenanceValidator.ModuleProvenanceFileName}",
            $$"""{"schemaVersion":1,"moduleName":"PSPublishModule","version":"3.0.81","repository":"{{Repository}}","commit":"{{commit}}"}""");
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
