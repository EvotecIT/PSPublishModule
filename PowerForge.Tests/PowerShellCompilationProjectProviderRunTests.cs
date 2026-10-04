namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_RunRejectsChangedProviderAndSemanticProfileBeforeExecution()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ProviderFixture.Create();
        fixture.BuildPackage("provider.nupkg");
        var source = Path.Combine(fixture.RootPath, "input.ps1");
        File.WriteAllText(source, "return 7");
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", "win-x64", false, true,
            PowerShellCompilationExecutableOptimization.None, true);
        var manifest = manifests.Create(project, source, "ProviderDevelopment", target);
        manifest.ProviderPackages = new[] { "provider.nupkg" };
        manifest.ProviderTrust = new PowerShellCompilationProviderTrustPolicy { AllowedPublishers = new[] { "Generic Publisher" } };
        manifest.Artifacts.Single().ProviderLock = ".powerforge/locks/providers.json";
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Ensure(workflow.Lock(project));
        Ensure(workflow.Restore(project));
        var options = new PowerShellCompilationProjectRunOptions { CaptureOutput = true, CaptureError = true };
        var run = await workflow.RunAsync(project, options);
        Assert.Empty(run.Error);
        Assert.Equal("7", run.Process!.StdOut.Trim());
        fixture.Manifest.PackageVersion = "1.0.1";
        fixture.BuildPackage("provider.nupkg");
        var drift = await workflow.RunAsync(project, options);
        Assert.Null(drift.Process);
        Assert.NotEmpty(drift.Error);
        Assert.False(workflow.Diagnose(project).Succeeded);

        var original = File.ReadAllText(project);
        File.WriteAllText(project, original.Replace("PowerForge.Oracle.PowerShell/7.6", "PowerForge.Oracle.PowerShell/7.4", StringComparison.Ordinal));
        var profileFailure = await Record.ExceptionAsync(() => workflow.RunAsync(project, options));
        Assert.NotNull(profileFailure);
        Assert.Contains("profile", profileFailure.Message, StringComparison.OrdinalIgnoreCase);

        static void Ensure(PowerShellCompilationProjectResult result)
            => Assert.True(result.Succeeded, string.Join("\n", result.Targets.Select(item => item.Message)));
    }
}
