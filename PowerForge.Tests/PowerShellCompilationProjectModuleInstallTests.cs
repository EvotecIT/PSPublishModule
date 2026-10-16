using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsTheory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("FormatsToProcess", ".ps1xml")]
    [InlineData("TypesToProcess", ".ps1xml")]
    [InlineData("NestedModules", ".cdxml")]
    [InlineData("RequiredAssemblies", ".exe")]
    [InlineData("RequiredAssemblies", ".bin")]
    public void Project_LegacyModuleInstallRejectsDeepManifestLoadedPayloadBeforeCreatingInstallation(
        string manifestKey, string extension)
    {
        using var fixture = ArtifactFixture.Create("'unused'", compactPath: true);
        var root = Path.Combine(fixture.RootPath, new string('p', 130 - fixture.RootPath.Length - 1));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "main.psm1");
        File.WriteAllText(source, "function Get-OwnedLegacyValue { return 42 }; Export-ModuleMember -Function Get-OwnedLegacyValue");
        var payloadName = new string('m', 80) + extension;
        var payloadPath = Path.Combine(root, payloadName);
        if (manifestKey == "RequiredAssemblies")
        {
            // RequiredAssemblies accepts managed PE files independently of their suffix.
            var frameworkDirectory = Environment.Is64BitOperatingSystem ? "Framework64" : "Framework";
            var frameworkAssembly = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET", frameworkDirectory, "v4.0.30319", "System.Core.dll");
            File.Copy(frameworkAssembly, payloadPath);
        }
        else
        {
            File.WriteAllText(payloadPath, manifestKey switch
            {
                "FormatsToProcess" => "<Configuration />",
                "TypesToProcess" => "<Types />",
                _ => """
                    <PowerShellMetadata xmlns="http://schemas.microsoft.com/cmdlets-over-objects/2009/11">
                      <Class ClassName="root/cimv2/Win32_OperatingSystem" ClassVersion="1.0">
                        <Version>1.0</Version><DefaultNoun>LegacyPathOperatingSystem</DefaultNoun>
                        <InstanceCmdlets><GetCmdletParameters /><GetCmdlet><CmdletMetadata Verb="Get" /></GetCmdlet></InstanceCmdlets>
                      </Class>
                    </PowerShellMetadata>
                    """
            });
        }
        File.WriteAllText(Path.ChangeExtension(source, ".psd1"),
            "@{ RootModule='main.psm1'; ModuleVersion='1.0.0'; " + manifestKey + "=@('./" + payloadName +
            "'); FunctionsToExport=@('Get-OwnedLegacyValue'); CmdletsToExport=@('*'); VariablesToExport=@(); AliasesToExport=@() }");
        var project = Path.Combine(root, "p.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            "net472", null, false, false, PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = manifests.Create(project, source, "D", target);
        var artifact = Assert.Single(manifest.Artifacts);
        artifact.Name = "x";
        artifact.ArtifactName = "D";
        artifact.OutputDirectory = "out";
        artifact.DependencyLock = ".powerforge/locks/x.lock.json";
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var primary = Assert.Single(build.Targets).Path!;
        AssertProjectSuccess(workflow.Test(project));
        var pack = workflow.Pack(project);
        AssertProjectSuccess(pack);
        var package = Assert.Single(pack.Targets).Path!;
        var outputRoot = Path.Combine(root, artifact.OutputDirectory);
        var builtPayload = Assert.Single(Directory.GetFiles(outputRoot, payloadName, SearchOption.AllDirectories));
        Assert.True(builtPayload.Length < 260);
        var installs = Path.Combine(root, ".powerforge", "i");
        using (var archive = ZipFile.OpenRead(package))
        {
            using var stream = archive.GetEntry("powerforge-package.json")!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var descriptorBytes = buffer.ToArray();
            using var descriptor = JsonDocument.Parse(descriptorBytes);
            var identity = PowerShellCompilationProjectWorkflowService.ComputeQualifiedInstallationIdentity(
                descriptor.RootElement.GetProperty("target").GetProperty("contractSha256").GetString()!,
                descriptor.RootElement.GetProperty("artifactSetSha256").GetString()!,
                Convert.ToHexString(SHA256.HashData(descriptorBytes)));
            var payloadRoot = Path.Combine(installs, identity, "artifact");
            Assert.True(Path.Combine(payloadRoot, Path.GetRelativePath(outputRoot, primary)).Length < 260,
                "The primary must remain loadable so this fixture reaches the additional manifest payload boundary.");
            var payloadRelative = Path.GetRelativePath(outputRoot, builtPayload).Replace('\\', '/');
            Assert.True(Path.Combine(payloadRoot, payloadRelative).Length >= 260);
            foreach (var file in descriptor.RootElement.GetProperty("files").EnumerateArray())
            {
                var relativePath = file.GetProperty("path").GetString()!;
                if (relativePath == payloadRelative) continue;
                Assert.True(Path.Combine(payloadRoot, relativePath).Length < 260,
                    $"Only the manifest-loaded fixture payload may cross the legacy path limit: {relativePath}.");
            }
        }
        var packageHash = PowerShellCompilationProjectManifestService.ComputeSha256(package);
        var primaryHash = PowerShellCompilationProjectManifestService.ComputeSha256(primary);
        var payloadHash = PowerShellCompilationProjectManifestService.ComputeSha256(builtPayload);
        var state = Directory.GetFiles(Path.Combine(root, ".powerforge"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), PowerShellCompilationProjectManifestService.ComputeSha256);
        Assert.False(Directory.Exists(installs));

        var install = workflow.Install(project);

        Assert.False(install.Succeeded);
        var message = Assert.Single(install.Targets).Message;
        Assert.Contains("Windows PowerShell 5.1", message, StringComparison.Ordinal);
        Assert.Contains("shorter than 260 characters", message, StringComparison.Ordinal);
        Assert.Contains(payloadName, message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(installs));
        Assert.Equal(packageHash, PowerShellCompilationProjectManifestService.ComputeSha256(package));
        Assert.Equal(primaryHash, PowerShellCompilationProjectManifestService.ComputeSha256(primary));
        Assert.Equal(payloadHash, PowerShellCompilationProjectManifestService.ComputeSha256(builtPayload));
        Assert.Equal(state.OrderBy(pair => pair.Key), Directory.GetFiles(Path.Combine(root, ".powerforge"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), PowerShellCompilationProjectManifestService.ComputeSha256)
            .OrderBy(pair => pair.Key));
    }

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
