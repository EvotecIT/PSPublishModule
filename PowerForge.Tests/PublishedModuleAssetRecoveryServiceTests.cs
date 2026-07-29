using System.IO.Compression;
using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed class PublishedModuleAssetRecoveryServiceTests
{
    [Fact]
    public void Restore_ReplacesModuleSubtreesWithPublishedPayloadAndPreservesFullPackageExtras()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SampleModule";
            const string version = "3.0.81";
            var packageBytes = CreatePublishedPackage(moduleName, version);
            var normalPath = Path.Combine(root.FullName, $"{moduleName}.v{version}.zip");
            var fullPath = Path.Combine(root.FullName, $"{moduleName}.v{version}-FullPackage.zip");
            CreateModuleArchive(normalPath, moduleName, includeExtras: false);
            CreateModuleArchive(fullPath, moduleName, includeExtras: true);

            var handler = new ModuleRecoveryHandler(packageBytes);
            var service = new PublishedModuleAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler));

            var restored = service.Restore(
                "https://gallery.example/v3/index.json",
                moduleName,
                version,
                [normalPath, fullPath],
                CancellationToken.None);

            Assert.Equal(new[] { normalPath, fullPath }, restored);
            AssertPublishedPayload(normalPath, moduleName);
            AssertPublishedPayload(fullPath, moduleName);
            using (var full = ZipFile.OpenRead(fullPath))
            {
                Assert.Equal(
                    "keep me",
                    ReadText(Assert.Single(full.Entries, entry => entry.FullName == "Examples/Example.ps1")));
                Assert.Equal(
                    "preserve dependency",
                    ReadText(Assert.Single(
                        full.Entries,
                        entry => entry.FullName == $"{moduleName}/Modules/Dependency/Dependency.psm1")));
            }
            Assert.Equal(
                [
                    "https://gallery.example/v3/index.json",
                    "https://gallery.example/v3-flatcontainer/samplemodule/3.0.81/samplemodule.3.0.81.nupkg"
                ],
                handler.RequestUris);
            Assert.Empty(Directory.GetFiles(root.FullName, "*.tmp"));
            Assert.Empty(Directory.GetFiles(root.FullName, "*.bak"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static byte[] CreatePublishedPackage(string moduleName, string version)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                $"{moduleName}.nuspec",
                $"<package><metadata><id>{moduleName}</id><version>{version}</version></metadata></package>");
            WriteEntry(archive, "[Content_Types].xml", "package metadata");
            WriteEntry(archive, ".signature.p7s", "gallery signature");
            WriteEntry(archive, $"{moduleName}.psd1", "published manifest");
            WriteEntry(archive, $"{moduleName}.psm1", "published signed payload");
            WriteEntry(archive, "Lib/Module.dll", "published binary");
        }
        return memory.ToArray();
    }

    private static void CreateModuleArchive(string path, string moduleName, bool includeExtras)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, $"{moduleName}/{moduleName}.psd1", "rebuilt manifest");
        WriteEntry(archive, $"{moduleName}/{moduleName}.psm1", "rebuilt timestamped payload");
        WriteEntry(archive, $"{moduleName}/Lib/Module.dll", "rebuilt binary");
        if (includeExtras)
        {
            WriteEntry(archive, "Examples/Example.ps1", "keep me");
            WriteEntry(
                archive,
                $"{moduleName}/Modules/Dependency/Dependency.psm1",
                "preserve dependency");
        }
    }

    private static void AssertPublishedPayload(string path, string moduleName)
    {
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(
            "published manifest",
            ReadText(Assert.Single(archive.Entries, entry => entry.FullName == $"{moduleName}/{moduleName}.psd1")));
        Assert.Equal(
            "published signed payload",
            ReadText(Assert.Single(archive.Entries, entry => entry.FullName == $"{moduleName}/{moduleName}.psm1")));
        Assert.Equal(
            "published binary",
            ReadText(Assert.Single(archive.Entries, entry => entry.FullName == $"{moduleName}/Lib/Module.dll")));
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class ModuleRecoveryHandler(byte[] packageBytes) : HttpMessageHandler
    {
        internal List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(uri);
            if (uri.EndsWith("/v3/index.json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"resources\":[{\"@id\":\"https://gallery.example/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes)
            });
        }
    }
}
