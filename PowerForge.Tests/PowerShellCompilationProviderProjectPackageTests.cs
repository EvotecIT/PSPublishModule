using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    private static void VerifyDependencyProviderProjectPackage(string providerPackage)
    {
        using var fixture = ScriptFixture.Create(
            "function Write-PackageDependency { Write-PackageDependencyCore 'locked' }", compactPath: true);
        File.Copy(providerPackage, Path.Combine(fixture.RootPath, "provider.nupkg"));
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, "net10.0", null, false, false,
            PowerShellCompilationExecutableOptimization.None, true);
        var manifest = manifests.Create(project, fixture.ScriptPath, "ProjectProvider", target);
        manifest.ProviderPackages = new[] { "provider.nupkg" };
        manifest.ProviderTrust.AllowedPublishers = new[] { "Generic Publisher" };
        manifest.Artifacts[0].ProviderLock = ".powerforge/locks/providers.json";
        manifest.Artifacts[0].EmitSource = true;
        manifest.NuGet = new PowerShellCompilationProjectNuGetPackage
        {
            PackageId = "Generated.ProviderDependency", PackageVersion = "1.0.0",
            Authors = "Provider fixture", Description = "Project provider closure consumer proof.", LicenseExpression = "MIT"
        };
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Check(workflow.Lock(project));
        Check(workflow.Explain(project));
        Check(workflow.Restore(project));
        Check(workflow.Build(project));
        Check(workflow.Test(project));
        Check(workflow.Diagnose(project));
        var packed = workflow.PackNuGet(project);
        Check(packed);
        var receipt = JsonSerializer.Deserialize<PowerShellCompilationBuildResult>(
            File.ReadAllText(Path.Combine(fixture.RootPath, ".powerforge/build", manifest.Artifacts[0].Name + ".json")),
            PowerShellCompilationProjectManifestService.JsonOptions)!;
        VerifyDependencyProviderFromLocalFeed(fixture.RootPath, receipt, packed.Targets.Single().Path!);
        var executableRoot = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "executable")).FullName;
        File.Copy(providerPackage, Path.Combine(executableRoot, "provider.nupkg"));
        var executableSource = Path.Combine(executableRoot, "entry.ps1");
        File.WriteAllText(executableSource, "Write-PackageDependencyCore 'locked'");
        var executableProject = Path.Combine(executableRoot, "powerforge.psproject.json");
        var executableTarget = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", "win-x64", false, false,
            PowerShellCompilationExecutableOptimization.None, true);
        var executableManifest = manifests.Create(executableProject, executableSource, "ProviderExplanation", executableTarget);
        executableManifest.ProviderPackages = manifest.ProviderPackages;
        executableManifest.ProviderTrust = manifest.ProviderTrust;
        manifests.Save(executableProject, executableManifest);
        Check(workflow.Explain(executableProject)); // Exercises the executable shaping owner with the reviewed provider.
        var originalPackage = File.ReadAllBytes(packed.Targets.Single().Path!);
        File.AppendAllText(Path.Combine(fixture.RootPath, "provider.nupkg"), "changed");
        Assert.False(workflow.PackNuGet(project).Succeeded);
        Assert.Equal(originalPackage, File.ReadAllBytes(packed.Targets.Single().Path!));

        static void Check(PowerShellCompilationProjectResult result)
            => Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Targets.Select(item => item.Message)));
    }
}
