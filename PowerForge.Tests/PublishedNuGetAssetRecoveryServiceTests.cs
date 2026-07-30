using System.IO.Compression;
using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed class PublishedNuGetAssetRecoveryServiceTests
{
    [Fact]
    public void Restore_ReplacesRebuiltPackageWithExactPublishedBytes()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            const string packageId = "PowerForge.Web.Build";
            const string version = "3.0.81";
            var packagePath = Path.Combine(root.FullName, $"{packageId}.{version}.nupkg");
            var releaseZipPath = Path.Combine(root.FullName, $"{packageId}.{version}.zip");
            var rebuiltBytes = CreatePackage(packageId, version, "rebuilt-timestamped-signature");
            var publishedBytes = CreatePackage(packageId, version, "published-original-signature");
            File.WriteAllBytes(packagePath, rebuiltBytes);
            CreateReleaseZip(releaseZipPath, packageId, "rebuilt-timestamped-signature");

            var handler = new NuGetRecoveryHandler(publishedBytes);
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler));

            var restored = service.Restore(
                "https://packages.example/v3/index.json",
                version,
                [packagePath, releaseZipPath],
                CancellationToken.None);

            Assert.Equal(new[] { packagePath, releaseZipPath }, restored);
            Assert.Equal(publishedBytes, File.ReadAllBytes(packagePath));
            Assert.NotEqual(rebuiltBytes, File.ReadAllBytes(packagePath));
            using (var releaseZip = ZipFile.OpenRead(releaseZipPath))
            {
                Assert.Equal(
                    "published-original-signature",
                    ReadText(Assert.Single(
                        releaseZip.Entries,
                        entry => entry.FullName == $"net10.0\\{packageId}.dll")));
                Assert.Equal(
                    "preserve dependency",
                    ReadText(Assert.Single(
                        releaseZip.Entries,
                        entry => entry.FullName == "net10.0\\Dependency.dll")));
            }
            Assert.Equal(
                [
                    "https://packages.example/v3/index.json",
                    "https://packages.example/v3-flatcontainer/powerforge.web.build/3.0.81/powerforge.web.build.3.0.81.nupkg"
                ],
                handler.RequestUris);
            Assert.Empty(Directory.GetFiles(root.FullName, "*.tmp"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restore_RejectsSymbolPackageBeforeDownloadingOrReplacingAnything()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "PowerForge.3.0.81.nupkg");
            var symbolPath = Path.Combine(root.FullName, "PowerForge.3.0.81.snupkg");
            var packageBytes = CreatePackage("PowerForge", "3.0.81", "rebuilt");
            File.WriteAllBytes(packagePath, packageBytes);
            File.WriteAllText(symbolPath, "symbol-package");
            var handler = new NuGetRecoveryHandler(CreatePackage("PowerForge", "3.0.81", "published"));
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler));

            var exception = Assert.Throws<InvalidOperationException>(() => service.Restore(
                "https://packages.example/v3/index.json",
                "3.0.81",
                [packagePath, symbolPath],
                CancellationToken.None));

            Assert.Contains("cannot prove byte identity", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.RequestUris);
            Assert.Equal(packageBytes, File.ReadAllBytes(packagePath));
            Assert.Equal("symbol-package", File.ReadAllText(symbolPath));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restore_MapsPublishedDotNetToolPayloadIntoReleaseZip()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            const string packageId = "PowerForge.Build";
            const string version = "3.0.81";
            var packagePath = Path.Combine(root.FullName, $"{packageId}.{version}.nupkg");
            var releaseZipPath = Path.Combine(root.FullName, $"{packageId}.{version}.zip");
            var rebuiltBytes = CreateToolPackage(packageId, version, "rebuilt-tool-payload");
            var publishedBytes = CreateToolPackage(packageId, version, "published-tool-payload");
            File.WriteAllBytes(packagePath, rebuiltBytes);
            using (var releaseZip = ZipFile.Open(releaseZipPath, ZipArchiveMode.Create))
            {
                WriteTextEntry(releaseZip, "net10.0/PowerForge.Cli.dll", "rebuilt-tool-payload");
                WriteTextEntry(releaseZip, "net10.0/PowerForge.dll", "rebuilt-tool-payload");
            }
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(new NuGetRecoveryHandler(publishedBytes)));

            var restored = service.Restore(
                "https://packages.example/v3/index.json",
                version,
                [packagePath, releaseZipPath],
                CancellationToken.None);

            Assert.Equal(new[] { packagePath, releaseZipPath }, restored);
            using var recoveredZip = ZipFile.OpenRead(releaseZipPath);
            Assert.Equal(
                "published-tool-payload",
                ReadText(Assert.Single(recoveredZip.Entries, entry => entry.FullName == "net10.0/PowerForge.Cli.dll")));
            Assert.Equal(
                "published-tool-payload",
                ReadText(Assert.Single(recoveredZip.Entries, entry => entry.FullName == "net10.0/PowerForge.dll")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restore_RetriesUntilNewlyPublishedPackageIsReadable()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            const string packageId = "PowerForge";
            const string version = "3.0.81";
            var packagePath = Path.Combine(root.FullName, $"{packageId}.{version}.nupkg");
            var publishedBytes = CreatePackage(packageId, version, "published");
            File.WriteAllBytes(packagePath, CreatePackage(packageId, version, "rebuilt"));
            var handler = new NuGetRecoveryHandler(publishedBytes, packageNotFoundResponses: 1);
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler),
                indexingTimeout: TimeSpan.FromSeconds(1),
                retryDelay: TimeSpan.Zero);

            service.Restore(
                "https://packages.example/v3/index.json",
                version,
                [packagePath],
                CancellationToken.None);

            Assert.Equal(3, handler.RequestUris.Count);
            Assert.Equal(publishedBytes, File.ReadAllBytes(packagePath));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restore_DoesNotRetryInvalidPublishedPayload()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var packagePath = Path.Combine(root.FullName, "PowerForge.3.0.81.nupkg");
            File.WriteAllBytes(packagePath, CreatePackage("PowerForge", "3.0.81", "rebuilt"));
            var handler = new NuGetRecoveryHandler(Encoding.UTF8.GetBytes("not a NuGet package"));
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler),
                indexingTimeout: TimeSpan.FromSeconds(1),
                retryDelay: TimeSpan.Zero);

            Assert.Throws<InvalidOperationException>(() => service.Restore(
                "https://packages.example/v3/index.json",
                "3.0.81",
                [packagePath],
                CancellationToken.None));

            Assert.Equal(2, handler.RequestUris.Count);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static byte[] CreatePackage(string packageId, string version, string signatureMarker)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nuspec = archive.CreateEntry($"{packageId}.nuspec");
            using (var writer = new StreamWriter(nuspec.Open(), new UTF8Encoding(false)))
            {
                writer.Write(
                    $"<package><metadata><id>{packageId}</id><version>{version}</version></metadata></package>");
            }

            var signature = archive.CreateEntry(".signature.p7s");
            using (var signatureWriter = new StreamWriter(signature.Open(), new UTF8Encoding(false)))
                signatureWriter.Write(signatureMarker);

            var payload = archive.CreateEntry($"lib/net10.0/{packageId}.dll");
            using (var payloadWriter = new StreamWriter(payload.Open(), new UTF8Encoding(false)))
                payloadWriter.Write(signatureMarker);
        }

        return memory.ToArray();
    }

    private static byte[] CreateToolPackage(string packageId, string version, string payload)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                $"{packageId}.nuspec",
                $"<package><metadata><id>{packageId}</id><version>{version}</version></metadata></package>");
            WriteTextEntry(archive, "tools/net10.0/any/DotnetToolSettings.xml", "package-only metadata");
            WriteTextEntry(archive, "tools/net10.0/any/PowerForge.Cli.dll", payload);
            WriteTextEntry(archive, "tools/net10.0/any/PowerForge.dll", payload);
        }
        return memory.ToArray();
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static void CreateReleaseZip(string path, string packageId, string payload)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var packageEntry = archive.CreateEntry($"net10.0\\{packageId}.dll");
        using (var writer = new StreamWriter(packageEntry.Open(), new UTF8Encoding(false)))
            writer.Write(payload);
        var dependency = archive.CreateEntry("net10.0\\Dependency.dll");
        using var dependencyWriter = new StreamWriter(dependency.Open(), new UTF8Encoding(false));
        dependencyWriter.Write("preserve dependency");
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class NuGetRecoveryHandler(
        byte[] packageBytes,
        int packageNotFoundResponses = 0) : HttpMessageHandler
    {
        internal List<string> RequestUris { get; } = [];
        private int _packageNotFoundResponses = packageNotFoundResponses;

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
                        "{\"resources\":[{\"@id\":\"https://packages.example/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (_packageNotFoundResponses-- > 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not indexed")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes)
            });
        }
    }
}
