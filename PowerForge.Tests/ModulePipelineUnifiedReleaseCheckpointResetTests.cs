using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_DiscardsPrePublishCheckpointAfterArtefactIdentityCorrection()
    {
        var fixture = CreatePrePublishArtefactIdentityFailure();
        try
        {
            var secondHosted = new FakeHostedOperations(new List<string>());
            var secondRunner = CreateRunner(secondHosted, fixture.ExecutePackageBuild);
            var secondSpec = CreateGalleryReleaseSpec(
                fixture.Root.FullName,
                fixture.SecondStagingPath,
                fixture.ModuleName);
            AddDuplicatePackedArtefacts(
                secondSpec,
                fixture.Root.FullName,
                fixture.ModuleName,
                useExplicitIds: true);

            var result = secondRunner.Run(secondSpec);

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.Equal(2, fixture.PackageBuildCount());
            Assert.Equal(new[] { "2.0.11" }, secondHosted.PublishedModuleVersions);
            AssertNoCoordinatedReleaseCheckpoint(fixture.Root.FullName);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Run_PreservesMalformedPrePublishCheckpointAfterArtefactIdentityCorrection()
    {
        var fixture = CreatePrePublishArtefactIdentityFailure();
        try
        {
            var checkpointJson = File.ReadAllText(fixture.CheckpointPath);
            checkpointJson = checkpointJson.Replace(
                "\"AttemptedOperations\": []",
                "\"AttemptedOperations\": null",
                StringComparison.Ordinal);
            File.WriteAllText(fixture.CheckpointPath, checkpointJson);

            AssertCorrectedRetryFailsClosed(fixture, "incomplete or invalid");
            Assert.True(File.Exists(fixture.CheckpointPath));
            Assert.Equal(1, fixture.PackageBuildCount());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Run_PreservesPrePublishCheckpointWithMalformedPlannedTopology()
    {
        var fixture = CreatePrePublishArtefactIdentityFailure();
        try
        {
            var checkpoint = JsonNode.Parse(File.ReadAllText(fixture.CheckpointPath))!;
            checkpoint["PlannedOperations"]![0] = "corrupt";
            File.WriteAllText(
                fixture.CheckpointPath,
                checkpoint.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            AssertCorrectedRetryFailsClosed(fixture, "configuration no longer matches");
            Assert.True(File.Exists(fixture.CheckpointPath));
            Assert.Equal(1, fixture.PackageBuildCount());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Run_DiscardsUnboundPayloadCacheWithResettablePrePublishCheckpoint()
    {
        var fixture = CreatePrePublishArtefactIdentityFailure();
        try
        {
            var payloadCachePath = Path.Combine(
                Path.GetDirectoryName(fixture.CheckpointPath)!,
                Path.GetFileNameWithoutExtension(fixture.CheckpointPath) + ".payload");
            Directory.CreateDirectory(payloadCachePath);
            File.WriteAllText(Path.Combine(payloadCachePath, "orphaned.bin"), "signed payload");

            var secondHosted = new FakeHostedOperations(new List<string>());
            var secondRunner = CreateRunner(secondHosted, fixture.ExecutePackageBuild);
            var secondSpec = CreateGalleryReleaseSpec(
                fixture.Root.FullName,
                fixture.SecondStagingPath,
                fixture.ModuleName);
            AddDuplicatePackedArtefacts(
                secondSpec,
                fixture.Root.FullName,
                fixture.ModuleName,
                useExplicitIds: true);

            var result = secondRunner.Run(secondSpec);

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.Equal(new[] { "2.0.11" }, secondHosted.PublishedModuleVersions);
            Assert.False(File.Exists(fixture.CheckpointPath));
            Assert.False(Directory.Exists(payloadCachePath));
            Assert.Equal(2, fixture.PackageBuildCount());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Run_UnlinksUnboundPayloadCacheReparsePointWithoutDeletingTarget()
    {
        var fixture = CreatePrePublishArtefactIdentityFailure();
        try
        {
            var payloadCachePath = Path.Combine(
                Path.GetDirectoryName(fixture.CheckpointPath)!,
                Path.GetFileNameWithoutExtension(fixture.CheckpointPath) + ".payload");
            if (Directory.Exists(payloadCachePath))
                Directory.Delete(payloadCachePath, recursive: true);

            var outsidePath = Path.Combine(fixture.Root.FullName, "outside-payload");
            Directory.CreateDirectory(outsidePath);
            var sentinelPath = Path.Combine(outsidePath, "sentinel.txt");
            File.WriteAllText(sentinelPath, "preserve");
            try
            {
                Directory.CreateSymbolicLink(payloadCachePath, outsidePath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var secondHosted = new FakeHostedOperations(new List<string>());
            var secondRunner = CreateRunner(secondHosted, fixture.ExecutePackageBuild);
            var secondSpec = CreateGalleryReleaseSpec(
                fixture.Root.FullName,
                fixture.SecondStagingPath,
                fixture.ModuleName);
            AddDuplicatePackedArtefacts(
                secondSpec,
                fixture.Root.FullName,
                fixture.ModuleName,
                useExplicitIds: true);

            var result = secondRunner.Run(secondSpec);

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.Equal(new[] { "2.0.11" }, secondHosted.PublishedModuleVersions);
            Assert.False(Directory.Exists(payloadCachePath));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal("preserve", File.ReadAllText(sentinelPath));
            Assert.Equal(2, fixture.PackageBuildCount());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static void AssertCorrectedRetryFailsClosed(
        PrePublishCheckpointFixture fixture,
        string expectedMessage)
    {
        var hosted = new FakeHostedOperations(new List<string>());
        var runner = CreateRunner(hosted, fixture.ExecutePackageBuild);
        var spec = CreateGalleryReleaseSpec(
            fixture.Root.FullName,
            fixture.SecondStagingPath,
            fixture.ModuleName);
        AddDuplicatePackedArtefacts(
            spec,
            fixture.Root.FullName,
            fixture.ModuleName,
            useExplicitIds: true);

        var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(hosted.PublishedModuleVersions);
    }

    private static PrePublishCheckpointFixture CreatePrePublishArtefactIdentityFailure()
    {
        const string moduleName = "TestModule";
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.Staging",
            Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.Staging",
            Guid.NewGuid().ToString("N"));
        var packageBuildCount = 0;

        ProjectBuildHostExecutionResult ExecutePackageBuild(
            ProjectBuildHostRequest request,
            ProjectBuildConfiguration? configuration,
            string? configPath)
        {
            packageBuildCount++;
            return CreateProjectBuildResult(
                root.FullName,
                moduleName,
                "2.0.11",
                Path.Combine(root.FullName, "Artifacts", "NuGet"),
                request,
                configPath,
                includePackage: false);
        }

        WriteMinimalModule(root.FullName, moduleName, "2.0.10");
        WriteSynchronizedProjectBuildConfig(
            root.FullName,
            "project.build.json",
            moduleName,
            publishNuGet: false);

        var hosted = new FakeHostedOperations(new List<string>());
        var runner = CreateRunner(hosted, ExecutePackageBuild);
        var spec = CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName);
        AddDuplicatePackedArtefacts(spec, root.FullName, moduleName, useExplicitIds: false);

        var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

        Assert.Contains("duplicate artefact identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(hosted.PublishedModuleVersions);
        var checkpointPath = Assert.Single(Directory.GetFiles(
            GetCoordinatedReleaseCheckpointRoot(root.FullName),
            "*.json"));
        var checkpointJson = File.ReadAllText(checkpointPath);
        Assert.Contains("\"Version\": \"2.0.11\"", checkpointJson, StringComparison.Ordinal);
        Assert.Contains("\"AttemptedOperations\": []", checkpointJson, StringComparison.Ordinal);
        Assert.Contains("\"CompletedOperations\": []", checkpointJson, StringComparison.Ordinal);
        Assert.Contains("\"AuxiliaryRemoteSideEffectsObserved\": false", checkpointJson, StringComparison.Ordinal);

        return new PrePublishCheckpointFixture(
            root,
            firstStagingPath,
            secondStagingPath,
            moduleName,
            checkpointPath,
            ExecutePackageBuild,
            () => packageBuildCount);
    }

    private sealed class PrePublishCheckpointFixture : IDisposable
    {
        public PrePublishCheckpointFixture(
            DirectoryInfo root,
            string firstStagingPath,
            string secondStagingPath,
            string moduleName,
            string checkpointPath,
            ModulePipelineRunnerDefaults.ModulePackageBuildExecutor executePackageBuild,
            Func<int> packageBuildCount)
        {
            Root = root;
            FirstStagingPath = firstStagingPath;
            SecondStagingPath = secondStagingPath;
            ModuleName = moduleName;
            CheckpointPath = checkpointPath;
            ExecutePackageBuild = executePackageBuild;
            PackageBuildCount = packageBuildCount;
        }

        public DirectoryInfo Root { get; }
        public string FirstStagingPath { get; }
        public string SecondStagingPath { get; }
        public string ModuleName { get; }
        public string CheckpointPath { get; }
        public ModulePipelineRunnerDefaults.ModulePackageBuildExecutor ExecutePackageBuild { get; }
        public Func<int> PackageBuildCount { get; }

        public void Dispose()
        {
            try { Root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(FirstStagingPath)) Directory.Delete(FirstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(SecondStagingPath)) Directory.Delete(SecondStagingPath, recursive: true); } catch { }
        }
    }
}
