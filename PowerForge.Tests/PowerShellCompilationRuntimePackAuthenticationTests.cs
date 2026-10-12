using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData("10.0.11", false)]
    [InlineData("10.0.0-preview.7.25380.108", false)]
    [InlineData("10.0.0-rc.2.25502.107", false)]
    [InlineData("10.0.0-rc.2.25502.107", true)]
    public void RuntimePackAuthorityRejectsCallerAuthoredArchivesEvenWithMatchingGraphAndCacheHashes(string version, bool native)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string id = "microsoft.netcore.app.runtime.linux-x64";
            var fileName = native ? "libcoreclr.so" : "System.Net.Ping.dll";
            var bytes = new byte[] { 1, 2, 3, 4 };
            var packageRoot = Directory.CreateDirectory(Path.Combine(root, id, version)).FullName;
            var archive = Path.Combine(packageRoot, id + "." + version + ".nupkg");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                    writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Microsoft</authors><description>Caller-authored impostor</description></metadata></package>");
                using var payload = zip.CreateEntry("runtimes/linux-x64/" + (native ? "native/" : "lib/net10.0/") + fileName).Open();
                payload.Write(bytes);
            }
            var graph = CreateRuntimePackAuthenticationGraph(id, version,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), native);
            File.WriteAllText(archive + ".sha512", Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archive))));
            var error = Assert.Throws<InvalidOperationException>(() => PowerShellCompilationRuntimePackAuthority.Authenticate(
                graph, "net10.0", "linux-x64", root, CancellationToken.None));
            Assert.Contains("authenticated author primary signature", error.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("microsoft.netcore.app.runtime.linux-x64", "not-a-version", "net10.0", "linux-x64")]
    [InlineData("microsoft.netcore.app.runtime.linux-x64", "10.0.0-rc..2", "net10.0", "linux-x64")]
    [InlineData("microsoft.netcore.app.runtime.linux-arm64", "10.0.0-rc.2.25502.107", "net10.0", "linux-x64")]
    [InlineData("microsoft.netcore.app.runtime.linux-x64", "10.0.0-rc.2.25502.107", "net9.0", "linux-x64")]
    [InlineData("microsoft.netcore.app.runtime.linux-x64", "10.0.0-rc.2.25502.107", "net10.0", "win-x64")]
    public void RuntimePackAuthorityRejectsInvalidVersionOrMismatchedTargetBeforeArchiveAuthentication(
        string id, string version, string targetFramework, string runtimeIdentifier)
    {
        var graph = CreateRuntimePackAuthenticationGraph(id, version, new string('0', 64));
        graph.Nodes[0].Identity.TargetFramework = targetFramework;
        graph.Nodes[0].Identity.RuntimeIdentifier = runtimeIdentifier;
        // No archive exists: invalid package identities must be refused before file access.
        var missingRoot = Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<InvalidOperationException>(() => PowerShellCompilationRuntimePackAuthority.Authenticate(
            graph, "net10.0", "linux-x64", missingRoot, CancellationToken.None));
        Assert.Contains("does not identify the selected target package", error.Message);
    }

    private static PowerShellCompilationDependencyGraph CreateRuntimePackAuthenticationGraph(
        string id, string version, string sha256, bool native = false)
        => new()
        {
            Nodes = new[] { new PowerShellCompilationDependencyNode
            {
                Id = "impostor", Exists = true,
                Kind = native ? PowerShellCompilationDependencyNodeKind.NativeLibrary : PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                Roles = PowerShellCompilationDependencyGraphRole.Deployment,
                Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = native ? "libcoreclr.so" : "System.Net.Ping", Version = "10.0.0.0", PublicKeyToken = "b03f5f7f11d50a3a",
                    TargetFramework = "net10.0", RuntimeIdentifier = "linux-x64", Provenance = "DotNetRuntimePack",
                    Source = "runtime-pack/" + id + "/" + version + "/" + (native ? "native/libcoreclr.so" : "System.Net.Ping.dll"),
                    Sha256 = sha256
                }
            } }
        };
}
