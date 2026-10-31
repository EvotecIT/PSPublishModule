using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseSigningFailureTests
{
    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, true)]
    public void Execute_AssemblySigningFailureStopsLaterProjectsAndPackageSigning(DotNetRepositoryPackStrategy strategy, bool failFast, bool cancel)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.SigningFailure.");
        try
        {
            foreach (var name in new[] { "First", "Second" })
            {
                var project = root.CreateSubdirectory(name);
                File.WriteAllText(Path.Combine(project.FullName, name + ".csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <VersionPrefix>1.0.0</VersionPrefix>
                        <IsPackable>true</IsPackable>
                      </PropertyGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(project.FullName, "Sample.cs"), "public class Sample { }");
            }
            var assemblyCalls = 0;
            var packageCalls = 0;
            using var cancellation = new CancellationTokenSource();
            var progress = new SigningProgressRecorder();
            DotNetRepositoryReleaseService.PackageSigningHandler packageSigner = (
                IReadOnlyList<string> packages, DotNetRepositoryReleaseSpec spec, string sha256,
                Action<string, int, DotNetNuGetSignResult?>? progress, out string[] failed, out string error) =>
            {
                packageCalls++;
                failed = packages.ToArray();
                error = "Unexpected package signing";
                return false;
            };
            var service = new DotNetRepositoryReleaseService(new NullLogger(), packageSigner, (_, _) => "ABCDEF");
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                OutputPath = Path.Combine(root.FullName, "packages"),
                PackStrategy = strategy,
                Pack = true,
                UpdateVersions = false,
                Publish = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = true,
                PublishFailFast = failFast
            };
            DotNetRepositoryReleaseResult Run() => service.Execute(spec, request =>
            {
                assemblyCalls++;
                var files = Assert.IsType<string[]>(request.FilePaths);
                Assert.NotEmpty(files);
                Assert.All(files, path => Assert.True(File.Exists(path), path));
                if (cancel)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                throw new InvalidOperationException("Synthetic signing denial");
            }, _ => { }, progress, cancellation.Token);

            if (cancel)
            {
                var error = Assert.ThrowsAny<OperationCanceledException>(() => Run());
                Assert.Equal(cancellation.Token, error.CancellationToken);
                Assert.Equal(1, assemblyCalls);
                Assert.Equal(0, packageCalls);
                return;
            }
            var result = Run();

            Assert.False(result.Success);
            Assert.Equal(2, result.Projects.Count(project => project.IsPackable));
            Assert.Equal(1, assemblyCalls);
            Assert.Equal(0, packageCalls);
            Assert.All(result.Projects, project => Assert.Empty(project.Packages));
            Assert.Empty(result.PublishedPackages);
            Assert.Contains("Synthetic signing denial", result.ErrorMessage, StringComparison.Ordinal);
            if (strategy == DotNetRepositoryPackStrategy.PerProject)
            {
                var packageRows = progress.Planned.Where(item => item.Phase == ProjectBuildProgressPhase.PackageBuild).ToArray();
                Assert.Equal(2, packageRows.Length);
                Assert.Equal(ProjectBuildProgressItemState.Failed, progress.States[packageRows[0].Key]);
                Assert.Equal(ProjectBuildProgressItemState.Skipped, progress.States[packageRows[1].Key]);
            }
        }
        finally { root.Delete(recursive: true); }
    }

    private sealed class SigningProgressRecorder : IProjectBuildProgressReporterV2
    {
        public List<ProjectBuildProgressItem> Planned { get; } = new();
        public Dictionary<string, ProjectBuildProgressItemState> States { get; } = new();
        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null) { }
        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null) { }
        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null) { }
        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null) { }
        public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items) => Planned.AddRange(items);
        public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null) => States[item.Key] = state;
    }
}
