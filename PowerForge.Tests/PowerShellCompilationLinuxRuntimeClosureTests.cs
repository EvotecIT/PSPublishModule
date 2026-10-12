using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [LinuxX64RuntimeClosureFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public Task Project_LinuxBundledSelfContainedRuntimeClosureRunsAfterInstall()
        => AssertLinuxRuntimeClosureAfterInstallAsync(PowerShellCompilationExecutableOptimization.None);

    [LinuxX64RuntimeClosureFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public Task Project_LinuxTrimmedRuntimeClosurePreservesAuthenticatedCallersAfterInstall()
        => AssertLinuxRuntimeClosureAfterInstallAsync(PowerShellCompilationExecutableOptimization.Trimmed);

    private static async Task AssertLinuxRuntimeClosureAfterInstallAsync(PowerShellCompilationExecutableOptimization optimization)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux runtime-pack closure qualification requires Linux.");
        // This exact bundled profile includes Linux System.Net.Ping's Process.Start
        // reference and CoreLib QCall while the SDK drops optional diagnostic binaries.
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            "net10.0", "linux-x64", selfContained: true, singleFile: true,
            optimization, explicitContract: true);
        var projectManifest = manifests.Create(project, fixture.ScriptPath, "LinuxRuntimeClosure", target);
        manifests.Save(project, projectManifest);
        await PowerShellCompilationTestPrerequisites.AcquireRuntimePackAsync("net10.0", "linux-x64");
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        if (optimization == PowerShellCompilationExecutableOptimization.None) AssertSignedRuntimePackAuthority(fixture, target);
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var primary = Assert.Single(build.Targets).Path!;
        var outputRoot = Path.GetDirectoryName(primary)!;
        using (var evidence = JsonDocument.Parse(File.ReadAllText(
                   Assert.Single(Directory.EnumerateFiles(outputRoot, "*.powerforge-compilation.json")))))
        {
            var manifest = evidence.RootElement;
            Assert.True(manifest.GetProperty("dependencyClosureVerified").GetBoolean());
            Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
            Assert.False(manifest.GetProperty("allowsPowerShellRuntimeEvaluation").GetBoolean());
            Assert.True(manifest.GetProperty("selfContained").GetBoolean());
            Assert.True(manifest.GetProperty("singleFile").GetBoolean());
            Assert.Equal(optimization.ToString(), manifest.GetProperty("optimization").GetString());
            var closure = manifest.GetProperty("dependencyClosure");
            Assert.True(closure.GetProperty("verified").GetBoolean());
            Assert.Equal("linux-x64", closure.GetProperty("runtimeIdentifier").GetString());
            if (optimization == PowerShellCompilationExecutableOptimization.None)
                Assert.Contains(closure.GetProperty("deliveredDependencies").EnumerateArray(),
                    dependency => dependency.GetProperty("identity").GetString()!.StartsWith("System.Net.Ping,", StringComparison.Ordinal));
            else
            {
                Assert.True(closure.GetProperty("transformedManagedAssemblies").GetInt32() > 0);
                var corelib = Assert.Single(closure.GetProperty("deliveredDependencies").EnumerateArray(),
                    dependency => dependency.GetProperty("identity").GetString()!.StartsWith("System.Private.CoreLib,", StringComparison.Ordinal));
                Assert.Equal("AuthenticatedSdkOptimization", corelib.GetProperty("derivation").GetString());
                Assert.DoesNotContain(corelib.GetProperty("reviewedInputSha256").EnumerateArray(),
                    input => input.GetString() == corelib.GetProperty("deliveredSha256").GetString());
                var cache = manifest.GetProperty("buildCache");
                Assert.False(cache.GetProperty("hit").GetBoolean());
                Assert.Equal("RequiresFreshAuthenticatedSdkPublish", cache.GetProperty("reason").GetString());
            }
            Assert.Contains(closure.GetProperty("reviewedNativeImports").EnumerateArray(),
                import => import.GetString() == "System.Private.CoreLib:QCall->AuthenticatedRuntimeIntrinsic");
            Assert.DoesNotContain(closure.GetProperty("targetAbiNativeImports").EnumerateArray(),
                import => import.GetString()!.Contains("QCall", StringComparison.Ordinal));
        }
        AssertProjectSuccess(workflow.Test(project));
        AssertProjectSuccess(workflow.Pack(project));
        var install = workflow.Install(project);
        AssertProjectSuccess(install);
        var installRoot = Assert.Single(install.Targets).Path!;
        var repeatedInstall = workflow.Install(project);
        AssertProjectSuccess(repeatedInstall);
        Assert.Equal(installRoot, Assert.Single(repeatedInstall.Targets).Path);
        var artifactOutputRoot = Path.Combine(fixture.RootPath, Assert.Single(projectManifest.Artifacts).OutputDirectory);
        var installed = Path.Combine(installRoot, "artifact", Path.GetRelativePath(artifactOutputRoot, primary));
        var run = RunProcess(installed);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("7", run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(primary),
            PowerShellCompilationProjectManifestService.ComputeSha256(installed));
        Assert.Equal(File.GetUnixFileMode(primary), File.GetUnixFileMode(installed));
    }

    private static void AssertSignedRuntimePackAuthority(ArtifactFixture fixture, PowerShellCompilationTargetContract target)
    {
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, Path.Combine(fixture.RootPath, "authority-out"),
            "LinuxRuntimeClosure", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict)
        {
            TargetContract = target
        };
        var graph = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var ping = Assert.Single(graph.Nodes, node => node.Identity.Name == "System.Net.Ping").Identity;
        var segments = ping.Source.Split('/');
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var isolatedRoot = Path.Combine(fixture.RootPath, "authenticated-pack");
        var versionRoot = Directory.CreateDirectory(Path.Combine(isolatedRoot, segments[1], segments[2])).FullName;
        var archiveName = segments[1] + "." + segments[2] + ".nupkg";
        var snapshotInput = Path.Combine(versionRoot, archiveName);
        File.Copy(Path.Combine(packageRoot, segments[1], segments[2], archiveName), snapshotInput);
        var authority = PowerShellCompilationRuntimePackAuthority.Authenticate(graph,
            "net10.0", "linux-x64", isolatedRoot, CancellationToken.None);
        Assert.True(authority.Matches(ping));
        Assert.False(authority.MatchesDelivered(ping, new string('0', 64), graph.LockSha256));
        Assert.False(authority.MatchesDelivered(ping, ping.Sha256, new string('0', 64)));

        var originalHash = ping.Sha256;
        ping.Sha256 = new string('0', 64);
        Assert.False(authority.Matches(ping));
        var changedEntry = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationRuntimePackAuthority.Authenticate(graph,
                "net10.0", "linux-x64", isolatedRoot, CancellationToken.None));
        Assert.Contains("authenticated package entry SHA-256", changedEntry.Message);
        ping.Sha256 = originalHash;
        ping.RuntimeIdentifier = "win-x64";
        Assert.False(authority.Matches(ping));
        ping.RuntimeIdentifier = "linux-x64";

        using (var changedArchive = System.IO.Compression.ZipFile.Open(snapshotInput, System.IO.Compression.ZipArchiveMode.Update))
        {
            var path = "runtimes/linux-x64/lib/net10.0/System.Net.Ping.dll";
            var entry = changedArchive.GetEntry(path)!;
            using var payload = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(payload);
            var bytes = payload.ToArray();
            bytes[bytes.Length - 1] ^= 1;
            entry.Delete();
            using var replacement = changedArchive.CreateEntry(path).Open();
            replacement.Write(bytes);
            ping.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        }
        Assert.False(authority.Matches(ping));
        var tampered = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationRuntimePackAuthority.Authenticate(graph,
                "net10.0", "linux-x64", isolatedRoot, CancellationToken.None));
        Assert.Contains("NuGet signature verification", tampered.Message);
        ping.Sha256 = originalHash;

        // The already-authenticated authority must survive replacement of its caller-owned
        // cache path. A new verification must reject the replacement, even with updated hashes.
        File.Delete(snapshotInput);
        using (var replacement = System.IO.Compression.ZipFile.Open(snapshotInput, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(replacement.CreateEntry(segments[1] + ".nuspec").Open()))
            writer.Write($"<package><metadata><id>{segments[1]}</id><version>{segments[2]}</version><authors>Microsoft</authors><description>Untrusted replacement</description></metadata></package>");
        Assert.True(authority.Matches(ping));
        var replaced = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationRuntimePackAuthority.Authenticate(graph,
                "net10.0", "linux-x64", isolatedRoot, CancellationToken.None));
        Assert.Contains("authenticated author primary signature", replaced.Message);
    }
}

internal sealed class LinuxX64RuntimeClosureFactAttribute : FactAttribute
{
    public LinuxX64RuntimeClosureFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Linux x64 runtime-pack closure qualification requires a Linux x64 host.";
    }
}
