using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseSigningSettingsServiceTests
{
    [Fact]
    public async Task MixedBuildUsesProjectCertificateOnlyForProjectArtifacts()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-mixed-signing-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            var config = Path.Combine(build, "project.build.json");
            File.WriteAllText(config, """{"CertificateThumbprint":"project-thumbprint","CertificateStore":"LocalMachine"}""");
            var package = Path.Combine(root, "Project.1.0.0.nupkg");
            var module = Path.Combine(root, "Module.ps1");
            File.WriteAllText(package, "package");
            File.WriteAllText(module, "# module");
            var result = new ReleaseBuildExecutionResult(root, true, "Built", 1,
                [new(ReleaseBuildAdapterKind.ProjectBuild, true, "Built", 0, 1, [], [package]),
                 new(ReleaseBuildAdapterKind.ModuleBuild, true, "Built", 0, 1, [], [module])],
                ProjectBuildConfigSha256: UnifiedReleaseConfigFingerprint.ComputeProjectBuildConfig(config));
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Ready", "sign.waiting.usb",
                JsonSerializer.Serialize(result), DateTimeOffset.UtcNow);
            var resolver = new ReleaseSigningHostSettingsResolver(
                name => name == "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" ? "host-thumbprint" : null,
                () => "module");
            var certificates = new CertificateFingerprintResolver((_, thumbprint) => thumbprint);
            AuthenticodeSigningHostRequest? moduleRequest = null;
            DotNetNuGetSignRequest? packageRequest = null;
            var readiness = new ReleaseSigningSettingsService(resolver, certificates).Check(item, result);
            Assert.True(readiness.IsAvailable, readiness.Status);
            Assert.Contains("ProjectBuild: Project JSON", readiness.Status);
            Assert.Contains("ModuleBuild: Host environment", readiness.Status);
            var noHost = new ReleaseSigningHostSettingsResolver(_ => null, () => "module");
            var missingHost = new ReleaseSigningSettingsService(noHost, certificates).Check(item, result);
            Assert.False(missingHost.IsAvailable);
            Assert.Contains("ProjectBuild: Project JSON signing certificate found", missingHost.Status);
            Assert.Contains("ModuleBuild: Host environment signing certificate is not configured", missingHost.Status);
            Assert.DoesNotContain("project.build.json", missingHost.Status);
            var unavailableCertificates = new CertificateFingerprintResolver((_, thumbprint) =>
                thumbprint == "PROJECT-THUMBPRINT" ? "PROJECT-FINGERPRINT" : null);
            var invokedBeforePreflight = false;
            var unavailableSigning = new ReleaseSigningExecutionService(new ReleaseBuildCheckpointReader(), resolver,
                unavailableCertificates, (_, _) => { invokedBeforePreflight = true; throw new InvalidOperationException("Unexpected signing"); },
                (_, _) => { invokedBeforePreflight = true; throw new InvalidOperationException("Unexpected signing"); });
            var unavailableResult = await unavailableSigning.ExecuteAsync(item);
            Assert.False(unavailableResult.Succeeded);
            Assert.False(invokedBeforePreflight);
            Assert.Contains("ModuleBuild signing certificate is unavailable", unavailableResult.Summary);
            var signing = new ReleaseSigningExecutionService(new ReleaseBuildCheckpointReader(), resolver,
                certificates, (request, _) => {
                    moduleRequest = request;
                    return Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 });
                }, (request, _) => {
                    packageRequest = request;
                    return Task.FromResult(new DotNetNuGetSignResult(0, "signed", "", "dotnet", TimeSpan.Zero, false, null));
                });
            var signed = await signing.ExecuteAsync(item);
            Assert.True(signed.Succeeded, signed.Summary);
            Assert.Equal("PROJECT-THUMBPRINT", packageRequest!.CertificateFingerprint);
            Assert.Equal("LocalMachine", packageRequest.CertificateStoreLocation);
            Assert.Equal("host-thumbprint", moduleRequest!.Thumbprint);
            Assert.Equal("CurrentUser", moduleRequest.StoreName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidProjectStoreFailsBeforeAnyArtifactIsSigned()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-invalid-signing-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            var config = Path.Combine(build, "project.build.json");
            File.WriteAllText(config, """{"CertificateThumbprint":"project-thumbprint","CertificateStore":"CurrentUser; Write-Host injected"}""");
            var package = Path.Combine(root, "Project.1.0.0.nupkg");
            File.WriteAllText(package, "package");
            var result = new ReleaseBuildExecutionResult(root, true, "Built", 1,
                [new(ReleaseBuildAdapterKind.ProjectBuild, true, "Built", 0, 1, [], [package])],
                ProjectBuildConfigSha256: UnifiedReleaseConfigFingerprint.ComputeProjectBuildConfig(config));
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Ready", "sign.waiting.usb",
                JsonSerializer.Serialize(result), DateTimeOffset.UtcNow);
            var resolver = new ReleaseSigningHostSettingsResolver(_ => null, () => "module");
            var readiness = new ReleaseSigningSettingsService(resolver, new CertificateFingerprintResolver()).Check(item, result);
            Assert.False(readiness.IsAvailable);
            var invoked = false;
            var signing = new ReleaseSigningExecutionService(new ReleaseBuildCheckpointReader(), resolver,
                new CertificateFingerprintResolver(), (_, _) => throw new InvalidOperationException("Unexpected signing"),
                (_, _) => { invoked = true; throw new InvalidOperationException("Unexpected signing"); });
            var signed = await signing.ExecuteAsync(item);
            Assert.False(signed.Succeeded);
            Assert.True(signed.RequiresRebuild);
            Assert.False(invoked);
            Assert.Contains("Rebuild before signing", signed.Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProjectJsonSelectsCertificateForSigningAndConfigDriftStopsTheCheckpoint()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-json-signing-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            var config = Path.Combine(build, "project.build.json");
            File.WriteAllText(config, """{"CertificateThumbprint":"project-thumbprint","CertificateStore":"LocalMachine","TimeStampServer":"https://timestamp.example.test"}""");
            var package = Path.Combine(root, "Fixture.1.0.0.nupkg");
            File.WriteAllText(package, "fixture package");
            var result = new ReleaseBuildExecutionResult(root, true, "Built", 1,
                [new ReleaseBuildAdapterResult(ReleaseBuildAdapterKind.ProjectBuild, true, "Built", 0, 1, [], [package])],
                ProjectBuildConfigSha256: UnifiedReleaseConfigFingerprint.ComputeProjectBuildConfig(config));
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Ready", "sign.waiting.usb",
                JsonSerializer.Serialize(result), DateTimeOffset.UtcNow);
            var resolver = new ReleaseSigningHostSettingsResolver(
                name => name == "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" ? "host-thumbprint" : null,
                () => "module");
            string? selectedThumbprint = null;
            DotNetNuGetSignRequest? signedRequest = null;
            var certificates = new CertificateFingerprintResolver((_, thumbprint) => {
                selectedThumbprint = thumbprint;
                return "ABC123";
            });
            var service = new ReleaseSigningExecutionService(new ReleaseBuildCheckpointReader(), resolver,
                certificates, (_, _) => Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 }),
                (request, _) => {
                    signedRequest = request;
                    return Task.FromResult(new DotNetNuGetSignResult(0, "signed", "", "dotnet", TimeSpan.Zero, false, null));
                });
            var readiness = new ReleaseSigningSettingsService(resolver, certificates);
            var ready = readiness.Check(item, result);
            Assert.True(ready.IsAvailable, ready.Status);
            Assert.Contains("Project JSON", ready.Status);

            var signed = await service.ExecuteAsync(item);
            Assert.True(signed.Succeeded, signed.Summary);
            Assert.Equal("PROJECT-THUMBPRINT", selectedThumbprint);
            Assert.Equal("LocalMachine", signedRequest!.CertificateStoreLocation);
            Assert.Equal("https://timestamp.example.test", signedRequest.TimeStampServer);

            File.WriteAllText(config, """{"CertificateThumbprint":"other-thumbprint"}""");
            var changed = readiness.Check(item, result);
            Assert.False(changed.IsAvailable);
            Assert.Contains("Rebuild before signing", changed.Status);
            signedRequest = null;
            var stale = await service.ExecuteAsync(item);
            Assert.False(stale.Succeeded);
            Assert.True(stale.RequiresRebuild);
            Assert.Null(signedRequest);
            Assert.Contains("Rebuild before signing", stale.Summary);
        }
        finally { Directory.Delete(root, true); }
    }
}
