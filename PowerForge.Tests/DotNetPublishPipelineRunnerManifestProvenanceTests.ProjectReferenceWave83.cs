using System.Text;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void Run_VisualStudioStoreBuildDoesNotResolveDotNet()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previousMsBuildPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        string? previousDotNetPath = Environment.GetEnvironmentVariable("POWERFORGE_DOTNET_PATH");
        try
        {
            string packagingProject = Path.Combine(root, "Store", "App.wapproj");
            string msBuildPath = Path.Combine(root, "VisualStudio", "MSBuild.exe");
            string outputDirectory = Path.Combine(root, "Artifacts", "Store");
            Directory.CreateDirectory(Path.GetDirectoryName(packagingProject)!);
            Directory.CreateDirectory(Path.GetDirectoryName(msBuildPath)!);
            File.WriteAllText(packagingProject, "<Project />", new UTF8Encoding(false));
            File.WriteAllText(msBuildPath, "trusted test host", new UTF8Encoding(false));
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", msBuildPath);
            Environment.SetEnvironmentVariable(
                "POWERFORGE_DOTNET_PATH",
                Path.Combine(root, "missing", "dotnet.exe"));

            ProcessRunRequest? captured = null;
            var runner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new RecordingProcessRunner(request =>
                {
                    captured = request;
                    Directory.CreateDirectory(outputDirectory);
                    File.WriteAllText(Path.Combine(outputDirectory, "App.msix"), "package");
                    return new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        request.FileName,
                        TimeSpan.Zero,
                        timedOut: false);
                }));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Restore = false,
                Build = false,
                StorePackages =
                [
                    new DotNetPublishStorePackagePlan
                    {
                        Id = "app.store",
                        PrepareFromTarget = "app",
                        PackagingProjectPath = packagingProject,
                        OutputPath = outputDirectory,
                        BuildMode = DotNetPublishStoreBuildMode.StoreUpload,
                        Bundle = DotNetPublishStoreBundleMode.Never
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "store.package:app.store:app:net8.0-windows:win-x64:FrameworkDependent",
                        Kind = DotNetPublishStepKind.StorePackage,
                        Title = "Store package",
                        StorePackageId = "app.store",
                        TargetName = "app",
                        Framework = "net8.0-windows",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent,
                        StorePackageProjectPath = packagingProject,
                        StorePackageOutputPath = outputDirectory
                    }
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Equal(Path.GetFullPath(msBuildPath), captured!.FileName);
            Assert.NotNull(captured.EnvironmentVariables);
            Assert.Null(captured.EnvironmentVariables!["MSBUILD_EXE_PATH"]);
            Assert.Null(captured.EnvironmentVariables["DOTNET_ROOT"]);
            Assert.Null(captured.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"]);
            Assert.Equal(
                "false",
                captured.EnvironmentVariables[
                    "ImportUserLocationsByWildcardAfterMicrosoftCommonTargets"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", previousMsBuildPath);
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", previousDotNetPath);
            DeleteTestRepository(root);
        }
    }
}
