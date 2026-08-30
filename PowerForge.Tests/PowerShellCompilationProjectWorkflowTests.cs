using System.IO.Compression;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationProjectWorkflowTests
{
    [Fact]
    public void ProjectWorkflow_UsesReviewedLocksOfflineEnvironmentAndQualifiedPackage()
    {
        using var fixture = ProjectFixture.Create();
        var manifestService = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            "net8.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var manifest = manifestService.Create(fixture.ProjectPath, fixture.ManifestPath, "GenericProject", target);
        manifestService.Save(fixture.ProjectPath, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();

        Assert.True(workflow.Analyze(fixture.ProjectPath).Succeeded);
        Assert.True(workflow.Explain(fixture.ProjectPath).Succeeded);
        Assert.True(workflow.Recommend(fixture.ProjectPath).Succeeded);
        Assert.True(workflow.Lock(fixture.ProjectPath).Succeeded);
        var acquired = workflow.Restore(fixture.ProjectPath);
        Assert.True(acquired.Succeeded, string.Join(Environment.NewLine, acquired.Targets.Select(static result => result.Message)));
        var offline = workflow.Restore(fixture.ProjectPath, offline: true);
        Assert.True(offline.Succeeded, string.Join(Environment.NewLine, offline.Targets.Select(static result => result.Message)));
        var build = workflow.Build(fixture.ProjectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Targets.Select(static result => result.Message)));
        Assert.True(workflow.Test(fixture.ProjectPath).Succeeded);
        Assert.True(workflow.Diagnose(fixture.ProjectPath).Succeeded);
        var pack = workflow.Pack(fixture.ProjectPath);
        Assert.True(pack.Succeeded);
        var packagePath = Assert.Single(pack.Targets).Path!;
        var packageHash = PowerShellCompilationProjectManifestService.ComputeSha256(packagePath);
        var repeatedPack = workflow.Pack(fixture.ProjectPath);
        Assert.True(repeatedPack.Succeeded);
        Assert.Equal(packageHash, PowerShellCompilationProjectManifestService.ComputeSha256(Assert.Single(repeatedPack.Targets).Path!));
        var install = workflow.Install(fixture.ProjectPath);
        Assert.True(install.Succeeded, string.Join(Environment.NewLine, install.Targets.Select(static result => result.Message)));
        var repeatedInstall = workflow.Install(fixture.ProjectPath);
        Assert.True(repeatedInstall.Succeeded, string.Join(Environment.NewLine, repeatedInstall.Targets.Select(static result => result.Message)));

        using var archive = ZipFile.OpenRead(packagePath);
        Assert.Contains(archive.Entries, static entry => entry.FullName == "powerforge-package.json");
        Assert.Contains(archive.Entries, static entry => entry.FullName.EndsWith(".powerforge-sbom.cdx.json", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, static entry => entry.FullName.EndsWith(".powerforge-provenance.json", StringComparison.Ordinal));
        Assert.All(archive.Entries, static entry => Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime));

        File.AppendAllText(Path.Combine(Assert.Single(install.Targets).Path!, "powerforge-package.json"), "tamper");
        var tamperedInstall = workflow.Install(fixture.ProjectPath);
        Assert.False(tamperedInstall.Succeeded);
        Assert.Contains("differs", Assert.Single(tamperedInstall.Targets).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectBuild_FailsWhenManifestChangesAfterEnvironmentAcquisition()
    {
        using var fixture = ProjectFixture.Create();
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            "net8.0",
            null,
            false,
            false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var manifest = service.Create(fixture.ProjectPath, fixture.ManifestPath, "DriftProject", target);
        service.Save(fixture.ProjectPath, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Assert.True(workflow.Lock(fixture.ProjectPath).Succeeded);
        var restored = workflow.Restore(fixture.ProjectPath);
        Assert.True(restored.Succeeded, string.Join(Environment.NewLine, restored.Targets.Select(static result => result.Message)));

        manifest.Diagnostics.RecommendedFailureBundleRetentionDays = 11;
        service.Save(fixture.ProjectPath, manifest);

        var exception = Assert.Throws<InvalidOperationException>(() => workflow.Build(fixture.ProjectPath));
        Assert.Contains("different project-manifest revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectManifest_RejectsDuplicateExactArtifactVariants()
    {
        using var fixture = ProjectFixture.Create();
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            "net8.0",
            null,
            false,
            false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var manifest = service.Create(fixture.ProjectPath, fixture.ManifestPath, "DuplicateProject", target);
        manifest.Artifacts = manifest.Artifacts.Concat(new[]
        {
            new PowerShellCompilationProjectArtifact
            {
                Name = "duplicate",
                Target = target,
                OutputDirectory = "artifacts/duplicate",
                DependencyLock = ".powerforge/locks/duplicate.lock.json"
            }
        }).ToArray();

        var exception = Assert.Throws<InvalidDataException>(() => service.Save(fixture.ProjectPath, manifest));
        Assert.Contains("duplicates another exact artifact variant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProjectFixture : IDisposable
    {
        private ProjectFixture(string root, string projectPath, string manifestPath)
        {
            Root = root;
            ProjectPath = projectPath;
            ManifestPath = manifestPath;
        }

        internal string Root { get; }
        internal string ProjectPath { get; }
        internal string ManifestPath { get; }

        internal static ProjectFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeProjectWorkflowTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var module = Path.Combine(root, "Generic.psm1");
            var manifest = Path.Combine(root, "Generic.psd1");
            File.WriteAllText(module, "function Get-GenericValue { [int] $value = 40; $value += 2; return $value }; Export-ModuleMember -Function Get-GenericValue");
            File.WriteAllText(manifest, "@{ RootModule = 'Generic.psm1'; ModuleVersion = '1.0.0'; GUID = '0c087599-00e7-4ab7-925e-a426c1914f55'; FunctionsToExport = @('Get-GenericValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");
            return new ProjectFixture(root, Path.Combine(root, "powerforge.psproject.json"), manifest);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
