namespace PowerForge.Tests;

[Trait("Category", "DotNetPublishPrGate")]
public sealed class DotNetPublishStagingCleanupTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RealPublish_CleansOnlyOwnedStagingAfterItsLastUse(bool useStaging, bool failPublish)
    {
        using var fixture = new TemporaryDirectory();
        string root = fixture.Path;
        string project = Path.Combine(root, "app.csproj");
        string output = Directory.CreateDirectory(Path.Combine(root, "installed")).FullName;
        File.WriteAllText(Path.Combine(output, "state.json"), "retained runtime state");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "global.json"), Path.Combine(root, "global.json"));
        File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"published payload\");");
        File.WriteAllText(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <UseAppHost>false</UseAppHost>
              </PropertyGroup>
              <Target Name="SyntheticPublishFailure" BeforeTargets="Publish" Condition="'{{failPublish}}' == 'True'">
                <MakeDir Directories="$(PublishDir)" />
                <WriteLinesToFile File="$(PublishDir)partial.tmp" Lines="partially published payload" />
                <Error Text="Synthetic staged publish failure" />
              </Target>
            </Project>
            """);

        string sibling = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.DotNetPublish", Guid.NewGuid().ToString("N"))).FullName;
        string sentinel = Path.Combine(sibling, "other-invocation.txt");
        File.WriteAllText(sentinel, "another invocation owns this payload");
        var processRunner = new CapturingProcessRunner();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                LockedOutputGuard = false,
                UseControlledSourceProvenance = true,
                EnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBUILDDISABLENODEREUSE"] = "1",
                    ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
                },
                Targets = new[]
                {
                    new DotNetPublishTargetPlan
                    {
                        Name = "app", ProjectPath = project,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0", Style = DotNetPublishStyle.FrameworkDependent,
                            UseStaging = useStaging, ClearOutput = false,
                            OutputPath = output, Slim = false, Zip = false
                        }
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "publish-app", Kind = DotNetPublishStepKind.Publish,
                        TargetName = "app", Framework = "net10.0", Runtime = string.Empty
                    }
                }
            };
            DotNetPublishResult result = new DotNetPublishPipelineRunner(new NullLogger(), processRunner).Run(plan, null);
            Assert.True(result.Succeeded == !failPublish, result.ErrorMessage);
            Assert.NotNull(processRunner.PublishDirectory);
            Assert.Equal("retained runtime state", File.ReadAllText(Path.Combine(output, "state.json")));
            Assert.Equal("another invocation owns this payload", File.ReadAllText(sentinel));

            if (failPublish)
            {
                Assert.Contains("Synthetic staged publish failure", processRunner.LastResult!.StdOut);
                Assert.Contains("Synthetic staged publish failure", result.ErrorMessage!);
                Assert.False(File.Exists(Path.Combine(output, "partial.tmp")));
            }
            else
            {
                var artifact = Assert.Single(result.Artefacts);
                Assert.True(File.Exists(Path.Combine(output, "app.dll")));
                Assert.Equal(processRunner.PublishDirectory, artifact.PublishDir);
                string checksums = Path.Combine(root, "SHA256SUMS");
                DotNetPublishPipelineRunner.WriteManifestsWithProvenance(
                    new DotNetPublishPlan { ProjectRoot = root, Outputs = new DotNetPublishOutputs { ChecksumsPath = checksums } },
                    new List<DotNetPublishArtefactResult> { artifact }, null, null,
                    verifiedSourceProvenance: new DotNetPublishPipelineRunner.SourceProvenance(null, null));
                string catalog = File.ReadAllText(checksums);
                Assert.Contains("app.dll", catalog);
                if (useStaging) Assert.DoesNotContain("state.json", catalog);
            }

            if (useStaging)
                Assert.False(Directory.Exists(processRunner.PublishDirectory), "Invocation-owned staging must be removed after success or failure.");
            else
                Assert.True(Directory.Exists(output), "Direct publish output must remain available.");
        }
        finally
        {
            // The baseline regression intentionally leaves its own staging behind.
            if (useStaging && processRunner.PublishDirectory is { } staging && Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.Delete(sibling, recursive: true);
        }
    }

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new(ownProcessTree: true);
        public string? PublishDirectory { get; private set; }
        public ProcessRunResult? LastResult { get; private set; }

        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            bool publishing = request.Arguments.Count > 0 && request.Arguments[0] == "publish";
            if (publishing)
            {
                int index = request.Arguments.ToList().IndexOf("--output");
                Assert.True(index >= 0);
                PublishDirectory = request.Arguments[index + 1];
            }
            ProcessRunResult result = await _inner.RunAsync(request, cancellationToken);
            if (publishing) LastResult = result;
            return result;
        }
    }
}
