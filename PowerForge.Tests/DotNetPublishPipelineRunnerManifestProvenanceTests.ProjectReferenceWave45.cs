using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectPathLoadedFromTrackedDataFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string inputPath = Path.Combine(root, "payload-path.txt");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                """
                <Project>
                  <Target Name="CopyPayload" BeforeTargets="Build">
                    <ReadLinesFromFile File="$(MSBuildThisFileDirectory)payload-path.txt">
                      <Output TaskParameter="Lines" ItemName="PayloadPath" />
                    </ReadLinesFromFile>
                    <Copy SourceFiles="@(PayloadPath)" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(inputPath, Path.Combine(externalRoot, "payload.dll"));

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));

            File.WriteAllText(inputPath, "artifacts/payload.dll");
            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildEnvironment_DropsUnusedConfiguredReplayValues()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["Payload"] = Path.Combine("..", "..", "outside", "payload.dll")
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> escapingEnvironment));
            Assert.False(escapingEnvironment.ContainsKey("Payload"));

            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["Payload"] = Path.Combine("artifacts", "payload.dll")
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> containedEnvironment));
            Assert.False(containedEnvironment.ContainsKey("Payload"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("XmlPeek")]
    [InlineData("JsonPeek")]
    public void ControlledBuildInputs_RejectOpaqueDataQueryTasks(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                $"<Project><Target Name=\"ReadPayload\" BeforeTargets=\"Build\"><{taskName} /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
