using System.Runtime.Versioning;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Run_SignedScriptArtefactRejectsUnixModeMutationAfterFinalization()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string helperSource = Path.Combine(root.FullName, "helper.sh");
            File.WriteAllText(helperSource, "#!/bin/sh\nprintf 'ok\\n'\n");
            File.SetUnixFileMode(
                helperSource,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                ActionStarted = (_, context) =>
                {
                    string helperPath = Path.Combine(Assert.Single(context.ArtefactPaths), "support", "helper.sh");
                    File.SetUnixFileMode(
                        helperPath,
                        File.GetUnixFileMode(helperPath) & ~UnixFileMode.UserExecute);
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Script"));
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.FilesOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = helperSource,
                    Destination = Path.Combine("support", "helper.sh")
                }
            ];
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Name = "remove helper execute permission",
                        At = ModulePipelineActionStage.AfterArtefacts,
                        InlineScript = "# executed through the test host"
                    }
                }
            }).ToArray();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("changed after signing", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("helper.sh", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
