using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_CleanLegacyModuleImportCannotAcceptDeepPayloadErrors()
    {
        using var fixture = ArtifactFixture.Create("'unused'", compactPath: true);
        var root = Path.Combine(fixture.RootPath, new string('p', 225 - fixture.RootPath.Length - 1));
        Directory.CreateDirectory(root);
        var name = "Legacy" + new string('m', 40);
        var module = Path.Combine(root, name + ".psm1");
        var manifest = Path.Combine(root, name + ".psd1");
        File.WriteAllText(module, "function Get-OwnedLegacyValue { return 42 }; Export-ModuleMember -Function Get-OwnedLegacyValue");
        File.WriteAllText(manifest, "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '1.0.0'; " +
            "FunctionsToExport = @('Get-OwnedLegacyValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");
        Assert.True(root.Length < 260);
        Assert.True(module.Length >= 260);
        var import = typeof(PowerShellCompilationProjectWorkflowService).GetMethod("ImportModule", BindingFlags.Static | BindingFlags.NonPublic)!;

        var failure = Assert.Throws<TargetInvocationException>(() => import.Invoke(null, new object[] { manifest, "net472" }));

        var error = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("Clean module import failed", error.Message, StringComparison.Ordinal);
    }

    [WindowsFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_LegacyModuleInstallRejectsDeepPayloadBeforeCreatingInstallation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-OwnedLegacyValue { return 42 }; Export-ModuleMember -Function Get-OwnedLegacyValue",
            ".psm1", compactPath: true);
        var root = Path.Combine(fixture.RootPath, new string('p', 170 - fixture.RootPath.Length - 1));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "main.psm1");
        File.Copy(fixture.ScriptPath, source);
        var project = Path.Combine(root, "p.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            "net472", null, false, false, PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = manifests.Create(project, source, "D", target);
        var artifact = Assert.Single(manifest.Artifacts);
        artifact.Name = "x";
        artifact.ArtifactName = "Legacy" + new string('m', 20);
        artifact.OutputDirectory = "out";
        artifact.DependencyLock = ".powerforge/locks/x.lock.json";
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var primary = Assert.Single(build.Targets).Path!;
        Assert.True(primary.Length < 260);
        AssertProjectSuccess(workflow.Test(project));
        var pack = workflow.Pack(project);
        AssertProjectSuccess(pack);
        var package = Assert.Single(pack.Targets).Path!;
        var packageHash = PowerShellCompilationProjectManifestService.ComputeSha256(package);
        var primaryHash = PowerShellCompilationProjectManifestService.ComputeSha256(primary);
        var installs = Path.Combine(root, ".powerforge", "i");
        Assert.False(Directory.Exists(installs));
        // Prove this package reaches the Desktop loader boundary using its actual
        // immutable qualification identity, rather than assuming the key's encoding length.
        using (var archive = ZipFile.OpenRead(package))
        {
            var entry = archive.GetEntry("powerforge-package.json")!;
            using var buffer = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(buffer);
            var descriptorBytes = buffer.ToArray();
            using var descriptor = JsonDocument.Parse(descriptorBytes);
            var identity = PowerShellCompilationProjectWorkflowService.ComputeQualifiedInstallationIdentity(
                descriptor.RootElement.GetProperty("target").GetProperty("contractSha256").GetString()!,
                descriptor.RootElement.GetProperty("artifactSetSha256").GetString()!,
                Convert.ToHexString(SHA256.HashData(descriptorBytes)));
            var relativePrimary = Path.GetRelativePath(Path.Combine(root, artifact.OutputDirectory), primary);
            Assert.Contains(descriptor.RootElement.GetProperty("files").EnumerateArray(),
                file => file.GetProperty("path").GetString() == relativePrimary.Replace('\\', '/'));
            var prospectivePrimary = Path.Combine(installs, identity, "artifact", relativePrimary);
            Assert.True(prospectivePrimary.Length >= 260,
                $"Fixture must exceed the installed Desktop payload limit; actual path length: {prospectivePrimary.Length}.");
        }

        var install = workflow.Install(project);

        Assert.False(install.Succeeded);
        var message = Assert.Single(install.Targets).Message;
        Assert.Contains("Windows PowerShell 5.1", message, StringComparison.Ordinal);
        Assert.Contains("shorter than 260 characters", message, StringComparison.Ordinal);
        Assert.Contains("Payload path length:", message, StringComparison.Ordinal);
        Assert.Contains("Shorten the project or output root", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(installs));
        Assert.Equal(packageHash, PowerShellCompilationProjectManifestService.ComputeSha256(package));
        Assert.Equal(primaryHash, PowerShellCompilationProjectManifestService.ComputeSha256(primary));
    }
}
