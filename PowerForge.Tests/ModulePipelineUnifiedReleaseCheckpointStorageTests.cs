using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void ResolveCheckpointStateRoot_UsesUserLocalStorageOutsideNonGitProject()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stateRoot = ModulePipelineRunner.ResolveSynchronizedReleaseStateRoot(root.FullName);
            var projectPrefix = Path.GetFullPath(root.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            Assert.False(stateRoot.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("PowerForge", stateRoot, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("coordinated-release-projects", stateRoot, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Run_RejectsConcurrentCoordinatedReleaseForSameModuleAsync()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        using var enteredPackageBuild = new ManualResetEventSlim(false);
        using var releasePackageBuild = new ManualResetEventSlim(false);
        Task<ModulePipelineResult>? firstRun = null;
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var firstRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    enteredPackageBuild.Set();
                    if (!releasePackageBuild.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Timed out waiting to complete the first package build.");
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                });
            firstRun = Task.Run(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.True(enteredPackageBuild.Wait(TimeSpan.FromSeconds(10)));

            var secondExecutorCalls = 0;
            var secondRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    secondExecutorCalls++;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                });
            var exception = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("already active", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, secondExecutorCalls);
        }
        finally
        {
            releasePackageBuild.Set();
            if (firstRun is not null)
                await firstRun;
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }
}
