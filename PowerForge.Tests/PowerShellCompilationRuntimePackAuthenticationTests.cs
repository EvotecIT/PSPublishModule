using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void RuntimePackAuthorityRejectsCallerAuthoredArchivesEvenWithMatchingGraphAndCacheHashes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string id = "microsoft.netcore.app.runtime.linux-x64";
            const string version = "10.0.11";
            const string fileName = "System.Net.Ping.dll";
            var bytes = new byte[] { 1, 2, 3, 4 };
            var packageRoot = Directory.CreateDirectory(Path.Combine(root, id, version)).FullName;
            var archive = Path.Combine(packageRoot, id + "." + version + ".nupkg");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                    writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Microsoft</authors><description>Caller-authored impostor</description></metadata></package>");
                using var payload = zip.CreateEntry("runtimes/linux-x64/lib/net10.0/" + fileName).Open();
                payload.Write(bytes);
            }
            var graph = new PowerShellCompilationDependencyGraph
            {
                Nodes = new[] { new PowerShellCompilationDependencyNode
                {
                    Id = "impostor", Exists = true, Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                    Roles = PowerShellCompilationDependencyGraphRole.Deployment,
                    Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored,
                    Identity = new PowerShellCompilationDependencyIdentity
                    {
                        Name = "System.Net.Ping", Version = "10.0.0.0", PublicKeyToken = "b03f5f7f11d50a3a",
                        TargetFramework = "net10.0", RuntimeIdentifier = "linux-x64", Provenance = "DotNetRuntimePack",
                        Source = "runtime-pack/" + id + "/" + version + "/" + fileName,
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    }
                } }
            };
            File.WriteAllText(archive + ".sha512", Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archive))));
            var error = Assert.Throws<InvalidOperationException>(() => PowerShellCompilationRuntimePackAuthority.Authenticate(
                graph, "net10.0", "linux-x64", root, CancellationToken.None));
            Assert.Contains("authenticated author primary signature", error.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
