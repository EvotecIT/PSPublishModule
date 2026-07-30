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
                new ManagedModuleRepositoryClient(
                    new NullLogger(),
                    new HttpClient(handler)));

            var restored = service.Restore(
                "https://www.powershellgallery.com/api/v2",
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
                    "https://cdn.powershellgallery.com/packages/samplemodule.3.0.81.nupkg"
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

    [Fact]
    public void Restore_RetriesUntilNewlyPublishedModuleIsReadable()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            const string moduleName = "SampleModule";
            const string version = "3.0.81";
            var archivePath = Path.Combine(root.FullName, $"{moduleName}.v{version}.zip");
            CreateModuleArchive(archivePath, moduleName, includeExtras: false);
            var handler = new ModuleRecoveryHandler(
                CreatePublishedPackage(moduleName, version),
                packageNotFoundResponses: 1);
            var service = new PublishedModuleAssetRecoveryService(
                new NullLogger(),
                new ManagedModuleRepositoryClient(new NullLogger(), new HttpClient(handler)),
                indexingTimeout: TimeSpan.FromSeconds(1),
                retryDelay: TimeSpan.Zero);

            service.Restore(
                "https://www.powershellgallery.com/api/v2",
                moduleName,
                version,
                [archivePath],
                CancellationToken.None);

            Assert.Equal(2, handler.RequestUris.Count);
            AssertPublishedPayload(archivePath, moduleName);
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
            WriteEntry(
                archive,
                PublishedRegistryProvenanceValidator.ModuleProvenanceFileName,
                $$"""{"schemaVersion":1,"moduleName":"{{moduleName}}","version":"{{version}}","repository":"https://github.com/EvotecIT/PSPublishModule","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""");
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

    private sealed class ModuleRecoveryHandler(
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

public sealed class RecoveryFileReplacementTransactionTests
{
    [Fact]
    public void Apply_PreservesBackupWhenRollbackCopyFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var first = Path.Combine(root.FullName, "first.zip");
            var second = Path.Combine(root.FullName, "second.zip");
            var firstReplacement = first + ".new";
            var secondReplacement = second + ".new";
            File.WriteAllText(first, "first-original");
            File.WriteAllText(second, "second-original");
            File.WriteAllText(firstReplacement, "first-replacement");
            File.WriteAllText(secondReplacement, "second-replacement");
            var firstRewrite = new RecoveryFileRewrite(first, firstReplacement);
            var secondRewrite = new RecoveryFileRewrite(second, secondReplacement);
            var replaceCount = 0;

            Assert.Throws<IOException>(() => RecoveryFileReplacementTransaction.Apply(
                [firstRewrite, secondRewrite],
                CancellationToken.None,
                copyFile: (source, destination, overwrite) =>
                {
                    if (overwrite && string.Equals(source, firstRewrite.BackupPath, StringComparison.Ordinal))
                        throw new IOException("simulated rollback failure");
                    File.Copy(source, destination, overwrite);
                },
                replaceFile: (source, destination) =>
                {
                    replaceCount++;
                    if (replaceCount == 2)
                        throw new IOException("simulated replacement failure");
                    File.Replace(source, destination, destinationBackupFileName: null);
                }));

            Assert.True(File.Exists(firstRewrite.BackupPath));
            Assert.False(firstRewrite.DeleteBackupOnCleanup);
            Assert.True(secondRewrite.DeleteBackupOnCleanup);
            Assert.Equal("first-original", File.ReadAllText(firstRewrite.BackupPath));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
