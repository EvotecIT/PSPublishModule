using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_TestAndInstallUseDeclaredArgumentsAndWorkingDirectoryWithRevisionBoundEvidence()
    {
        using var fixture = ArtifactFixture.Create("""
            param([Parameter(Mandatory)] [string] $Expected)
            if ([System.IO.File]::ReadAllText('input.txt') -ne $Expected) { throw [System.InvalidOperationException]::new('Invocation input differs') }
            return 7
            """, compactPath: true);
        const string expected = "Zażółć O'Brien \"quoted\"; $HOME &";
        const string workingDirectory = "data O'Brien Zażółć";
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, workingDirectory));
        File.WriteAllText(Path.Combine(fixture.RootPath, workingDirectory, "input.txt"), expected);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = CreateInvocationManifest(service, project, fixture);
        var artifact = Assert.Single(manifest.Artifacts);
        artifact.TestArguments = new[] { "--Expected=" + expected };
        artifact.TestWorkingDirectory = workingDirectory;
        service.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        AssertProjectSuccess(workflow.Test(project));
        var package = workflow.Pack(project);
        AssertProjectSuccess(package);
        var packagePath = Assert.Single(package.Targets).Path!;
        var packageBytes = File.ReadAllBytes(packagePath);
        var install = workflow.Install(project);
        AssertProjectSuccess(install);
        var installation = Assert.Single(install.Targets).Path!;
        var repeated = workflow.Install(project);
        AssertProjectSuccess(repeated);
        Assert.Equal(installation, Assert.Single(repeated.Targets).Path);
        var output = Path.Combine(fixture.RootPath, artifact.OutputDirectory);
        var primaryRelative = Path.GetRelativePath(output, Assert.Single(build.Targets).Path!);
        var installedPrimary = Path.Combine(installation, "artifact", primaryRelative);
        var installedHash = PowerShellCompilationProjectManifestService.ComputeSha256(installedPrimary);
        var builtPrimary = Assert.Single(build.Targets).Path!;
        var descriptorPath = Path.Combine(installation, "powerforge-package.json");
        var originalDescriptor = File.ReadAllBytes(descriptorPath);

        // Repeating successful qualification without changing its inputs preserves the package and root.
        AssertProjectSuccess(workflow.Test(project));
        AssertProjectSuccess(workflow.Pack(project));
        Assert.Equal(packageBytes, File.ReadAllBytes(packagePath));
        var retestedInstall = workflow.Install(project);
        AssertProjectSuccess(retestedInstall);
        Assert.Equal(installation, Assert.Single(retestedInstall.Targets).Path);

        // Equivalent execution from a newly declared directory qualifies unchanged artifact bytes separately.
        const string equivalentDirectory = "equivalent data";
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, equivalentDirectory));
        File.WriteAllText(Path.Combine(fixture.RootPath, equivalentDirectory, "input.txt"), expected);
        artifact.TestWorkingDirectory = equivalentDirectory;
        service.Save(project, manifest);
        AssertProjectSuccess(workflow.Restore(project, offline: true));
        AssertProjectSuccess(workflow.Test(project));
        AssertProjectSuccess(workflow.Pack(project));
        var requalifiedInstall = workflow.Install(project);
        AssertProjectSuccess(requalifiedInstall);
        var requalifiedRoot = Assert.Single(requalifiedInstall.Targets).Path!;
        Assert.NotEqual(installation, requalifiedRoot);
        Assert.Equal(installedHash, PowerShellCompilationProjectManifestService.ComputeSha256(builtPrimary));
        Assert.Equal(installedHash, PowerShellCompilationProjectManifestService.ComputeSha256(installedPrimary));
        Assert.Equal(originalDescriptor, File.ReadAllBytes(descriptorPath));
        Assert.False(packageBytes.SequenceEqual(File.ReadAllBytes(packagePath)));
        installedPrimary = Path.Combine(requalifiedRoot, "artifact", primaryRelative);
        Assert.Equal(installedHash, PowerShellCompilationProjectManifestService.ComputeSha256(installedPrimary));
        using (var before = JsonDocument.Parse(originalDescriptor))
        using (var after = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(requalifiedRoot, "powerforge-package.json"))))
        {
            Assert.Equal(before.RootElement.GetProperty("artifactSetSha256").GetString(), after.RootElement.GetProperty("artifactSetSha256").GetString());
            Assert.NotEqual(before.RootElement.GetProperty("projectSha256").GetString(), after.RootElement.GetProperty("projectSha256").GetString());
            Assert.NotEqual(before.RootElement.GetProperty("testEvidenceSha256").GetString(), after.RootElement.GetProperty("testEvidenceSha256").GetString());
        }
        var repeatedRequalifiedInstall = workflow.Install(project);
        AssertProjectSuccess(repeatedRequalifiedInstall);
        Assert.Equal(requalifiedRoot, Assert.Single(repeatedRequalifiedInstall.Targets).Path);
        packageBytes = File.ReadAllBytes(packagePath);
        var evidencePath = Path.Combine(fixture.RootPath, ".powerforge", "test", artifact.Name + ".json");
        var originalEvidence = File.ReadAllBytes(evidencePath);
        using var evidence = JsonDocument.Parse(originalEvidence);
        var testedRevision = evidence.RootElement.GetProperty("projectSha256").GetString();
        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(project), testedRevision);

        foreach (var changeArguments in new[] { true, false })
        {
            artifact.TestArguments = new[] { "--Expected=" + (changeArguments ? "changed" : expected) };
            artifact.TestWorkingDirectory = changeArguments ? equivalentDirectory : ".";
            service.Save(project, manifest);
            Assert.NotEqual(testedRevision, PowerShellCompilationProjectManifestService.ComputeSha256(project));
            // Reacquisition must not make old successful execution evidence qualify a new invocation.
            AssertProjectSuccess(workflow.Restore(project, offline: true));
            var stalePack = workflow.Pack(project);
            var staleInstall = workflow.Install(project);
            Assert.False(stalePack.Succeeded);
            Assert.False(staleInstall.Succeeded);
            Assert.Contains("test evidence", Assert.Single(stalePack.Targets).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("test evidence", Assert.Single(staleInstall.Targets).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(packageBytes, File.ReadAllBytes(packagePath));
            Assert.Equal(originalEvidence, File.ReadAllBytes(evidencePath));
            Assert.Equal(installedHash, PowerShellCompilationProjectManifestService.ComputeSha256(installedPrimary));
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_OfflineRestoreRejectsTamperedHistoricalEnvironmentAfterInvocationEdit()
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = CreateInvocationManifest(service, project, fixture);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        service.Save(project, manifest);
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var artifact = Assert.Single(manifest.Artifacts);
        var environmentPath = Path.Combine(fixture.RootPath, ".powerforge", "environment", "environment.json");
        var environment = JsonSerializer.Deserialize<PowerShellCompilationProjectEnvironment>(
            File.ReadAllText(environmentPath), PowerShellCompilationProjectManifestService.JsonOptions)!;
        var package = environment.Packages.First();
        var archivePath = Path.Combine(environment.PackageRoot, package.Id.ToLowerInvariant(),
            package.Version.ToLowerInvariant(), package.Id.ToLowerInvariant() + "." + package.Version.ToLowerInvariant() + ".nupkg");
        var archiveSha = PowerShellCompilationProjectManifestService.ComputeSha256(archivePath);
        var lockPath = Path.Combine(fixture.RootPath, Assert.Single(environment.ResolvedLocks).Path);
        var lockBytes = File.ReadAllBytes(lockPath);

        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "requalified"));
        artifact.TestWorkingDirectory = "requalified";
        service.Save(project, manifest);
        var projectBytes = File.ReadAllBytes(project);
        Assert.NotEqual(environment.ProjectSha256, PowerShellCompilationProjectManifestService.ComputeSha256(project));
        environment.EnvironmentSha256 = new string('0', 64);
        File.WriteAllText(environmentPath, JsonSerializer.Serialize(environment, PowerShellCompilationProjectManifestService.JsonOptions));
        var tamperedEnvironmentBytes = File.ReadAllBytes(environmentPath);

        var restored = workflow.Restore(project, offline: true);

        Assert.False(restored.Succeeded);
        Assert.Contains("environment evidence differs from its recorded content identity", Assert.Single(restored.Targets).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(tamperedEnvironmentBytes, File.ReadAllBytes(environmentPath));
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        Assert.Equal(lockBytes, File.ReadAllBytes(lockPath));
        Assert.Equal(archiveSha, PowerShellCompilationProjectManifestService.ComputeSha256(archivePath));
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".powerforge", "build")));
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".powerforge", "i")));
    }

    [Theory]
    [InlineData("../outside", "escapes the project root")]
    [InlineData("absolute", "must be relative")]
    [InlineData("missing", "not found")]
    [InlineData("input.ps1", "working directory")]
    public void Project_TestWorkingDirectoryRejectsUncontainedOrNonDirectoryPaths(string directory, string message)
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = CreateInvocationManifest(service, project, fixture);
        Assert.Single(manifest.Artifacts).TestWorkingDirectory = directory == "absolute" ? fixture.RootPath : directory;

        var exception = Assert.ThrowsAny<Exception>(() => service.Save(project, manifest));

        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(project));
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".powerforge", "i")));
    }

    [Fact]
    public void Project_TestWorkingDirectoryRejectsLinkedDirectoryInsideProject()
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = CreateInvocationManifest(service, project, fixture);
        var actual = Path.Combine(fixture.RootPath, "actual");
        var linked = Path.Combine(fixture.RootPath, "linked");
        Directory.CreateDirectory(actual);
        try
        {
            Directory.CreateSymbolicLink(linked, actual);
            Assert.Single(manifest.Artifacts).TestWorkingDirectory = "linked";

            var exception = Assert.Throws<InvalidOperationException>(() => service.Save(project, manifest));

            Assert.Contains("symbolic link or junction", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(project));
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
        }
    }

    [Theory]
    [InlineData("null-vector")]
    [InlineData("null-element")]
    [InlineData("nul")]
    public void Project_TestArgumentsRejectsInvalidProcessArgumentVectors(string kind)
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = CreateInvocationManifest(service, project, fixture);
        Assert.Single(manifest.Artifacts).TestArguments = kind switch
        {
            "null-vector" => null!,
            "null-element" => new[] { (string)null! },
            _ => new[] { "--Expected=a\0b" }
        };

        var exception = Assert.Throws<InvalidDataException>(() => service.Save(project, manifest));

        Assert.Contains("test arguments", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(project));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_TestInvocationOptionsRequireExecutableTargets(bool arguments)
    {
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict, "net10.0",
            runtimeIdentifier: null, selfContained: false, singleFile: false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = service.Create(project, fixture.ScriptPath, "Invocation", target);
        var artifact = Assert.Single(manifest.Artifacts);
        if (arguments) artifact.TestArguments = new[] { "--Expected=7" };
        else artifact.TestWorkingDirectory = ".";

        var exception = Assert.Throws<InvalidDataException>(() => service.Save(project, manifest));

        Assert.Contains("require an executable target", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(project));
    }

    private static PowerShellCompilationProjectManifest CreateInvocationManifest(
        PowerShellCompilationProjectManifestService service, string project, ArtifactFixture fixture)
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            "net10.0", RuntimeInformation.RuntimeIdentifier, selfContained: false, singleFile: false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        return service.Create(project, fixture.ScriptPath, "Invocation", target);
    }
}
