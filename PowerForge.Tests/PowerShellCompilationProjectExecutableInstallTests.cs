using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_ExecutablePackagePreservesQualifiedPermissionsAndRunsAfterInstall()
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            "net10.0", RuntimeInformation.RuntimeIdentifier, selfContained: false, singleFile: false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = manifests.Create(project, fixture.ScriptPath, "QualifiedExecutable", target);
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var primary = Assert.Single(build.Targets).Path!;
        AssertExecutable(primary);
        if (!OperatingSystem.IsWindows())
        {
            RejectPrivilegedSourceModes(() => workflow.Test(project));
            var unqualifiedPack = workflow.Pack(project);
            Assert.False(unqualifiedPack.Succeeded);
            Assert.Contains("project test", Assert.Single(unqualifiedPack.Targets).Message, StringComparison.OrdinalIgnoreCase);
        }
        AssertProjectSuccess(workflow.Test(project));

        if (!OperatingSystem.IsWindows())
        {
            RejectPrivilegedSourceModes(() => workflow.Pack(project));
            var mode = File.GetUnixFileMode(primary);
            try
            {
                File.SetUnixFileMode(primary, mode & ~ExecuteBits);
                var changedAfterTest = workflow.Pack(project);
                Assert.False(changedAfterTest.Succeeded);
                Assert.Contains("test evidence", Assert.Single(changedAfterTest.Targets).Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.SetUnixFileMode(primary, mode);
            }
        }

        var pack = workflow.Pack(project);
        AssertProjectSuccess(pack);
        var package = Assert.Single(pack.Targets).Path!;
        var originalPackage = File.ReadAllBytes(package);
        AssertProjectSuccess(workflow.Pack(project));
        Assert.Equal(originalPackage, File.ReadAllBytes(package));

        var install = workflow.Install(project);
        AssertProjectSuccess(install);
        var installRoot = Assert.Single(install.Targets).Path!;
        var outputRoot = Path.Combine(fixture.RootPath, Assert.Single(manifest.Artifacts).OutputDirectory);
        var relativePrimary = Path.GetRelativePath(outputRoot, primary);
        var installedPrimary = Path.Combine(installRoot, "artifact", relativePrimary);
        AssertExecutable(installedPrimary);
        var repeatedInstall = workflow.Install(project);
        AssertProjectSuccess(repeatedInstall);
        Assert.Equal(installRoot, Assert.Single(repeatedInstall.Targets).Path);
        if (!OperatingSystem.IsWindows())
        {
            RejectPrivilegedSourceModes(() => workflow.Install(project));
            Assert.Equal(originalPackage, File.ReadAllBytes(package));
        }

        var entryName = "artifact/" + relativePrimary.Replace('\\', '/');
        int archiveAttributes;
        using (var archive = ZipFile.OpenRead(package))
            archiveAttributes = archive.GetEntry(entryName)!.ExternalAttributes;

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(primary);
            Assert.NotEqual((UnixFileMode)0, mode & ExecuteBits);
            Assert.Equal(mode & (UnixFileMode)0x1FF, File.GetUnixFileMode(installedPrimary));
            Assert.Equal((int)mode & 0x1FF, archiveAttributes >> 16 & 0x1FF);
            try
            {
                File.SetUnixFileMode(installedPrimary, mode & ~ExecuteBits);
                var changedInstallation = workflow.Install(project);
                Assert.False(changedInstallation.Succeeded);
                Assert.Contains("Unix permission bits", Assert.Single(changedInstallation.Targets).Message, StringComparison.Ordinal);
            }
            finally
            {
                File.SetUnixFileMode(installedPrimary, mode);
            }

            RejectArchiveAttributes(archiveAttributes & ~((int)ExecuteBits << 16), "Unix permission bits");
        }

        RejectArchiveAttributes(archiveAttributes | (0x800 << 16), "privileged Unix permission bits");
        RejectArchiveAttributes((archiveAttributes & ~(0xF000 << 16)) | (0xA000 << 16), "symbolic link");
        AssertProjectSuccess(workflow.Install(project));
        AssertExecutable(installedPrimary);

        void RejectPrivilegedSourceModes(Func<PowerShellCompilationProjectResult> operation)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix mode checks require a Unix host.");
            var mode = File.GetUnixFileMode(primary);
            foreach (var privileged in new[] { UnixFileMode.SetUser, UnixFileMode.SetGroup, UnixFileMode.StickyBit })
            {
                try
                {
                    File.SetUnixFileMode(primary, mode | privileged);
                    Assert.Equal(mode | privileged, File.GetUnixFileMode(primary));
                    var rejected = operation();
                    Assert.False(rejected.Succeeded);
                    Assert.Contains("privileged Unix permission bits", Assert.Single(rejected.Targets).Message, StringComparison.Ordinal);
                }
                finally
                {
                    File.SetUnixFileMode(primary, mode);
                }
            }
        }

        void RejectArchiveAttributes(int attributes, string message)
        {
            try
            {
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
                    archive.GetEntry(entryName)!.ExternalAttributes = attributes;
                var rejected = workflow.Install(project);
                Assert.False(rejected.Succeeded);
                Assert.Contains(message, Assert.Single(rejected.Targets).Message, StringComparison.Ordinal);
            }
            finally
            {
                using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
                archive.GetEntry(entryName)!.ExternalAttributes = archiveAttributes;
            }
        }

        static void AssertExecutable(string path)
        {
            var run = RunProcess(path);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("7", run.StandardOutput.Trim());
            Assert.Empty(run.StandardError);
        }
    }

    private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
}
