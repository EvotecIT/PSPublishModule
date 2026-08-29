using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_NoBuildPublishKeepsPublicDefiningProjectMetadataBeforeSnapshotCopyBinding()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                propertyXml: null,
                """
                <Target Name="CaptureOriginalDefiningProject" BeforeTargets="_ComputeResolvedFilesToPublishTypes">
                  <ItemGroup>
                    <PowerForgeAppItem Include="@(ResolvedFileToPublish)"
                                       Condition="'%(ResolvedFileToPublish.RelativePath)' == 'App.dll'" />
                  </ItemGroup>
                  <Error Condition="'%(PowerForgeAppItem.DefiningProjectName)' == 'PowerForge.NoBuildPublishInputs'"
                         Text="The public publish item was replaced before project hooks observed its defining project." />
                </Target>
                """,
                out string outputDirectory,
                out _);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "App.dll")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedDotNetInstallationSnapshot_ObservesFrameworkAndWorkloadPackRoots()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            string sdkDirectory = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100")).FullName;
            string packFile = CreateFixtureFile(root, "packs", "Microsoft.NETCore.App.Ref", "10.0.0", "ref.dll");
            string manifestFile = CreateFixtureFile(root, "sdk-manifests", "10.0.100", "fixture", "WorkloadManifest.json");
            string workloadFile = CreateFixtureFile(root, "metadata", "workloads", "installed.json");
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(Path.Combine(sdkDirectory, "MSBuild.dll"), "trusted-sdk");

            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(executablePath, root);

            Assert.True(snapshot.AffectsCapturedClosureForTest(packFile));
            Assert.True(snapshot.AffectsCapturedClosureForTest(manifestFile));
            Assert.True(snapshot.AffectsCapturedClosureForTest(workloadFile));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedNativeAotPathSnapshot_RejectsCompilerReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string toolPath = Path.Combine(root, OperatingSystem.IsWindows() ? "link.exe" : "clang");
            File.WriteAllText(toolPath, "trusted-native-tool");
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    toolPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
#endif
            using DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot.Create(root);

            File.WriteAllText(toolPath, "replaced-native-tool");

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.ValidateUnchanged(verifyHashes: true));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedNativeAotPathSnapshot_IgnoresUntrackedNonExecutableFiles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string toolPath = Path.Combine(root, "link.exe");
            string libraryPath = Path.Combine(root, "unrelated.dll");
            File.WriteAllText(toolPath, "trusted-native-tool");
            File.WriteAllText(libraryPath, "unrelated-library");

            using DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot.Create(root);

            Assert.True(snapshot.AffectsNativeToolForTest(toolPath));
            Assert.True(snapshot.AffectsNativeToolForTest(Path.Combine(root, "new-shim.cmd")));
            Assert.False(snapshot.AffectsNativeToolForTest(libraryPath));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static string CreateFixtureFile(string root, params string[] parts)
    {
        string path = parts.Aggregate(root, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return path;
    }
}
