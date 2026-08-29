using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_NoBuildPublishRebindsAfterDownstreamPublishItemHook()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                propertyXml: null,
                """
                <Target Name="RebindOriginalApp" BeforeTargets="CopyFilesToPublishDirectory">
                  <ItemGroup>
                    <ResolvedFileToPublish Remove="@(ResolvedFileToPublish)" Condition="'%(ResolvedFileToPublish.RelativePath)' == 'App.dll'" />
                    <ResolvedFileToPublish Include="$(MSBuildProjectDirectory)/bin/Release/net8.0/App.dll" RelativePath="App.dll" CopyToPublishDirectory="Always" />
                  </ItemGroup>
                </Target>
                """,
                out string outputDirectory,
                out byte[] provenAppBytes,
                (_, appOutput, bytes) => new RestoringProjectReferenceOutputRunner(appOutput, bytes));

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(provenAppBytes, File.ReadAllBytes(Path.Combine(outputDirectory, "App.dll")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void Run_NoBuildPublishRejectsTransientTrackedProjectReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string ignoredPayload = Path.Combine(root, "bin", "unproven.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredPayload)!);
            File.WriteAllText(ignoredPayload, "unproven payload");
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                propertyXml: null,
                targetXml: null,
                out _,
                out _,
                (projectPath, _, _) => new TemporarilyReplacingProjectRunner(
                    projectPath,
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net8.0</TargetFramework>
                      </PropertyGroup>
                      <ItemGroup>
                        <Content Include="bin/unproven.dll" CopyToPublishDirectory="Always" TargetPath="unproven.dll" />
                      </ItemGroup>
                    </Project>
                    """));

            Assert.False(result.Succeeded);
            string errorMessage = result.ErrorMessage ?? string.Empty;
            Assert.True(
                errorMessage.Contains("proven", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase),
                errorMessage);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private sealed class TemporarilyReplacingProjectRunner(
        string projectPath,
        string replacementProject) : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            string originalProject = File.ReadAllText(projectPath);
            try
            {
                File.WriteAllText(projectPath, replacementProject);
                return await _inner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    File.WriteAllText(projectPath, originalProject);
                }
                catch (IOException)
                {
                    // The immutable Windows lease is released when the failed run unwinds.
                }
            }
        }
    }
}
