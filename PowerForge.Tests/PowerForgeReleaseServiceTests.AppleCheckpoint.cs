namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_MixedRelease_CreatesAppleArchiveBeforeSigningCheckpoint()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveCalled = false;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    RootPath = root,
                    Result = new ProjectBuildResult()
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveCalled = true;
                    Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(request.ArchivePath!, "Info.plist"), "checkpointed");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run while checkpointing."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration { RootPath = "." },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = true,
                        Upload = true,
                        Apps =
                        [
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        ]
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    CheckpointAppleApps = true
                });

            Assert.True(result.Success);
            Assert.True(archiveCalled);
            var appResult = Assert.Single(result.AppleApps);
            Assert.NotNull(appResult.Archive);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.False(plan.Archive);
            Assert.True(plan.Upload);
            Assert.True(Directory.Exists(Assert.Single(plan.Apps).ArchivePath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedRelease_CheckpointsReusableAppleArchiveWithoutUploading()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archivePath = Path.Combine(
                root,
                "Artifacts",
                "Apple",
                "Archives",
                "iOS",
                "Tactra.xcarchive");
            Directory.CreateDirectory(archivePath);
            File.WriteAllText(Path.Combine(archivePath, "Info.plist"), "approved");
            var packagesExecuted = false;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    packagesExecuted = true;
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        RootPath = root,
                        Result = new ProjectBuildResult()
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Archive should not run."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run while checkpointing."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration { RootPath = "." },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        Upload = true,
                        Apps =
                        [
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        ]
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    CheckpointAppleApps = true
                });

            Assert.True(result.Success);
            Assert.True(packagesExecuted);
            Assert.Empty(result.AppleApps);
            Assert.Equal(archivePath, Assert.Single(result.AppleAppPlan!.Apps).ArchivePath);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
