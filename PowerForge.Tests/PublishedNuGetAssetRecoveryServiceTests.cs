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
            var rebuiltBytes = CreatePackage(packageId, version, "rebuilt-timestamped-signature");
            var publishedBytes = CreatePackage(packageId, version, "published-original-signature");
            File.WriteAllBytes(packagePath, rebuiltBytes);

            var handler = new NuGetRecoveryHandler(publishedBytes);
            var service = new PublishedNuGetAssetRecoveryService(
                new NullLogger(),
                new NuGetV3PackageDownloader(handler));

            var restored = service.Restore(
                "https://packages.example/v3/index.json",
                version,
                [packagePath],
                CancellationToken.None);

            Assert.Equal(packagePath, Assert.Single(restored));
            Assert.Equal(publishedBytes, File.ReadAllBytes(packagePath));
            Assert.NotEqual(rebuiltBytes, File.ReadAllBytes(packagePath));
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
            using var signatureWriter = new StreamWriter(signature.Open(), new UTF8Encoding(false));
            signatureWriter.Write(signatureMarker);
        }

        return memory.ToArray();
    }

    private sealed class NuGetRecoveryHandler(byte[] packageBytes) : HttpMessageHandler
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
                        "{\"resources\":[{\"@id\":\"https://packages.example/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}",
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
