namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ModuleBuildPreparationPublicationCheckpointTests {
    [Fact]
    public void Prepare_plan_publish_rejects_a_substituted_checkpoint_before_upload() {
        const string digestVariable = "POWERFORGE_MODULE_PUBLICATION_CHECKPOINT_SHA256";
        string? previous = Environment.GetEnvironmentVariable(digestVariable);
        var root = Directory.CreateTempSubdirectory("pf-prepared-checkpoint-");
        try {
            string source = Directory.CreateDirectory(Path.Combine(root.FullName, "Module")).FullName;
            string staging = Path.Combine(root.FullName, "staging");
            string config = Path.Combine(root.FullName, "powerforge.json");
            File.WriteAllText(Path.Combine(source, "Sample.psm1"), "function Get-Sample { 'ok' }");
            File.WriteAllText(Path.Combine(source, "Sample.psd1"),
                "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.2.3'; FunctionsToExport = @('Get-Sample'); CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(config, """
                {
                  "SchemaVersion": 1,
                  "Build": { "Name": "Sample", "SourcePath": "Module", "Version": "1.2.3", "KeepStaging": true },
                  "Install": { "Enabled": false },
                  "Segments": [{ "Type": "GalleryNuget", "Configuration": {
                    "Enabled": true, "Destination": "PowerShellGallery", "Tool": "PSResourceGet",
                    "Force": true, "ApiKey": "fixture-token"
                  }}]
                }
                """);
            int uploads = 0;
            var runner = new ModulePipelineRunner(new NullLogger(), new CheckpointPublishRunner(request => {
                string script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("PFPSRG::AVAILABLE::", StringComparison.Ordinal))
                    return new PowerShellRunResult(0, "PFPSRG::AVAILABLE::1", string.Empty, "pwsh.exe");
                Assert.Contains("Publish-PSResource", script, StringComparison.Ordinal);
                Assert.Equal(File.ReadAllBytes(Path.Combine(staging, "Sample.psm1")),
                    File.ReadAllBytes(Path.Combine(request.Arguments[0], "Sample.psm1")));
                uploads++;
                return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", string.Empty, "pwsh.exe");
            }));
            var preparation = new ModuleBuildPreparationService();
            var request = new ModuleBuildPreparationRequest {
                ParameterSetName = "Config", ConfigPath = config, CurrentPath = root.FullName,
                ResolvePath = path => path, StagingPath = staging, ReleaseCheckpoint = true,
                NoDotnetBuild = true, NoDotnetBuildWasBound = true, NoSign = true, NoSignWasBound = true,
                SkipInstall = true, IncludeProjectPackages = false, RunMode = ConfigurationGateMode.Build
            };
            var build = preparation.Prepare(request);
            runner.Run(build.PipelineSpec);
            string checkpoint = staging + ".powerforge-publication.json";
            byte[] original = File.ReadAllBytes(checkpoint);
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
            Environment.SetEnvironmentVariable(digestVariable, digest);
            request.ReuseStaging = true;
            request.RunMode = ConfigurationGateMode.Publish;
            var publish = preparation.Prepare(request);
            var plan = runner.Plan(publish.PipelineSpec);
            // A valid JSON replacement leaves every module file and persisted payload hash unchanged.
            File.AppendAllText(checkpoint, "\n ");

            var error = Assert.Throws<InvalidOperationException>(() => runner.Run(publish.PipelineSpec, plan));

            Assert.Contains("changed a release input", error.Message, StringComparison.Ordinal);
            Assert.Contains(checkpoint, error.Message, StringComparison.Ordinal);
            Assert.Equal(0, uploads);
            File.WriteAllBytes(checkpoint, original);
            var result = runner.Run(publish.PipelineSpec, plan);
            Assert.True(Assert.Single(result.PublishResults).Succeeded);
            Assert.Equal(1, uploads);
        } finally {
            Environment.SetEnvironmentVariable(digestVariable, previous);
            root.Delete(recursive: true);
        }
    }

    private sealed class CheckpointPublishRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }
}
