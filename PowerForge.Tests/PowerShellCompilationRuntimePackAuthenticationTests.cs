using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [WindowsOrLinuxX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task AuthenticatedPackageRequiresOneExactSignedNuspecLayout()
    {
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        await PowerShellCompilationTestPrerequisites.AcquireRuntimePackAsync("net10.0", rid);
        var version = PowerShellCompilationToolchainFingerprint.ResolveRuntimePackVersion("net10.0");
        var id = "Microsoft.NETCore.App.Runtime." + rid;
        var cacheRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(cacheRoot))
            cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var package = PowerShellCompilationRuntimePackAuthority.AuthenticateMicrosoftPackage(
            id, version, cacheRoot, CancellationToken.None);
        package.VerifyInstalled(cacheRoot!);

        byte[] signedNuspec;
        string archiveName;
        var archivePath = Path.Combine(cacheRoot!, id.ToLowerInvariant(), version,
            id.ToLowerInvariant() + "." + version + ".nupkg");
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var entry = Assert.Single(archive.Entries.Where(static entry => entry.FullName.IndexOf('/') < 0 &&
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)));
            archiveName = entry.FullName;
            using var stream = entry.Open();
            using var payload = new MemoryStream();
            stream.CopyTo(payload);
            signedNuspec = payload.ToArray();
        }
        Assert.True(package.Matches(archiveName, Convert.ToHexString(SHA256.HashData(signedNuspec))),
            "The metadata fixture must be the exact authenticated archive entry.");
        var canonicalName = id.ToLowerInvariant() + ".nuspec";
        Assert.NotEqual(canonicalName, archiveName);
        using var workspace = PowerShellCompilationWorkspace.Create(keep: false);
        var stageRoot = Path.Combine(workspace.Path, "authenticated-packages");
        await package.InstallAsync(stageRoot, CancellationToken.None);
        var directory = Path.Combine(stageRoot, id.ToLowerInvariant(), version);

        string SetNuspecName(string name)
        {
            var existing = Assert.Single(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(signedNuspec, File.ReadAllBytes(existing));
            var transition = Path.Combine(workspace.Path, "nuspec-transition");
            File.Move(existing, transition);
            var selected = Path.Combine(directory, name);
            File.Move(transition, selected);
            Assert.Equal(name, Path.GetFileName(Assert.Single(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)))));
            Assert.Equal(signedNuspec, File.ReadAllBytes(selected));
            package.VerifyInstalled(stageRoot);
            return selected;
        }

        SetNuspecName(archiveName);
        var canonicalPath = SetNuspecName(canonicalName);
        var changedNuspec = (byte[])signedNuspec.Clone();
        changedNuspec[changedNuspec.Length - 1] ^= 1;
        try
        {
            File.WriteAllBytes(canonicalPath, changedNuspec);
            Assert.Throws<InvalidOperationException>(() => package.VerifyInstalled(stageRoot));
        }
        finally { File.WriteAllBytes(canonicalPath, signedNuspec); }
        package.VerifyInstalled(stageRoot);

        var withdrawn = Path.Combine(workspace.Path, "withdrawn.nuspec");
        try
        {
            File.Move(canonicalPath, withdrawn);
            Assert.Throws<InvalidOperationException>(() => package.VerifyInstalled(stageRoot));
        }
        finally { File.Move(withdrawn, canonicalPath); }
        package.VerifyInstalled(stageRoot);

        var originalPath = Path.Combine(directory, archiveName);
        // Distinct-case collisions are representable on case-sensitive validation volumes.
        if (!File.Exists(originalPath))
        {
            File.Copy(canonicalPath, originalPath);
            Assert.Equal(2, Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Count(static path => path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)));
            Assert.Throws<InvalidOperationException>(() => package.VerifyInstalled(stageRoot));
        }
    }

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
            var archiveHash = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archive)));
            File.WriteAllText(archive + ".sha512", archiveHash);
            File.WriteAllText(Path.Combine(packageRoot, ".nupkg.metadata"), JsonSerializer.Serialize(new { contentHash = archiveHash }));
            var error = Assert.Throws<InvalidOperationException>(() => PowerShellCompilationRuntimePackAuthority.Authenticate(
                graph, "net10.0", "linux-x64", root, CancellationToken.None));
            Assert.Contains("authenticated author primary signature", error.Message);
            var acquiredError = Assert.Throws<InvalidOperationException>(() =>
                PowerShellCompilationNuGetPackageVerifier.ReadPackage(root, id, version));
            Assert.Contains("authenticated author primary signature", acquiredError.Message);
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

    [FileSymbolicLinkFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NuGetPackageVerificationRejectsByteIdenticalLinkedControlFiles()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string id = "microsoft.netcore.app.runtime.linux-x64";
            const string version = "10.0.11";
            var cache = Directory.CreateDirectory(Path.Combine(root, "private-cache")).FullName;
            var packageRoot = Directory.CreateDirectory(Path.Combine(cache, id, version)).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside-private-cache")).FullName;
            var archive = Path.Combine(packageRoot, id + "." + version + ".nupkg");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Caller</authors><description>Isolation fixture</description></metadata></package>");
            var hash = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archive)));
            File.WriteAllText(archive + ".sha512", hash);
            var metadata = Path.Combine(packageRoot, ".nupkg.metadata");
            File.WriteAllText(metadata, JsonSerializer.Serialize(new { contentHash = hash }));
            foreach (var controlFile in new[] { archive, archive + ".sha512", metadata })
            {
                var original = File.ReadAllBytes(controlFile);
                var externalFile = Path.Combine(outside, Path.GetFileName(controlFile));
                File.WriteAllBytes(externalFile, original);
                File.Delete(controlFile);
                try
                {
                    File.CreateSymbolicLink(controlFile, externalFile);
                    Assert.True(File.GetAttributes(controlFile).HasFlag(FileAttributes.ReparsePoint));
                    Assert.Equal(original, File.ReadAllBytes(controlFile));
                    var acquisition = Assert.Throws<InvalidOperationException>(() =>
                        PowerShellCompilationNuGetPackageVerifier.ReadPackage(cache, id, version));
                    Assert.Contains("symbolic link or junction", acquisition.Message);
                    var verification = Assert.Throws<InvalidOperationException>(() =>
                        PowerShellCompilationNuGetPackageVerifier.Verify(cache, new PowerShellCompilationProjectPackage
                        {
                            Id = id, Version = version, ContentHash = hash
                        }));
                    Assert.Contains("symbolic link or junction", verification.Message);
                }
                finally
                {
                    if (File.Exists(controlFile)) File.Delete(controlFile);
                    File.WriteAllBytes(controlFile, original);
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // xUnit v2 has no dynamic SkipException support. Resolve this one filesystem
    // prerequisite through the ordinary Fact.Skip contract before running the fact.
    private sealed class FileSymbolicLinkFactAttribute : FactAttribute
    {
        public FileSymbolicLinkFactAttribute()
        {
            var root = Path.Combine(Path.GetTempPath(), "PFC", "link-probe-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(target, "File symbolic link prerequisite");
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Skip = "File symbolic link prerequisite is unavailable: " + exception.GetType().Name;
            }
            finally
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(target)) File.Delete(target);
                if (Directory.Exists(root)) Directory.Delete(root);
            }
        }
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
