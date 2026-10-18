using System.Security.Cryptography;
using System.Reflection;

namespace PowerForge.Tests;

[Trait("Category", "DotNetPublishPrGate")]
public sealed class DotNetPublishChecksumInputsTests
{
    [Fact]
    public void Publish_RetainsStagingInventoryWhenDestinationIsPreserved()
    {
        string root = Directory.CreateTempSubdirectory("PowerForge.Checksums.").FullName;
        string? staging = null;
        try
        {
            string output = Directory.CreateDirectory(Path.Combine(root, "installed")).FullName;
            string project = Path.Combine(root, "app.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(output, "old.dll"), "pre-existing runtime file");
            var target = new DotNetPublishTargetPlan
            {
                Name = "app", ProjectPath = project, ExecutableIdentities = new[] { "app" },
                Publish = new DotNetPublishPublishOptions
                {
                    UseStaging = true, ClearOutput = false, Zip = false, OutputPath = output,
                    Service = new DotNetPublishServicePackageOptions { ServiceName = "Synthetic.Checksum.Test" }
                }
            };
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root, Targets = new[] { target }, LockedOutputGuard = false,
                UseControlledSourceProvenance = true
            };
            var runner = new DotNetPublishPipelineRunner(new NullLogger(), new PayloadProcessRunner(path => staging = path));
            MethodInfo publish = typeof(DotNetPublishPipelineRunner).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var artifact = Assert.IsType<DotNetPublishArtefactResult>(publish.Invoke(runner, new object?[]
            {
                plan, "app", "net10.0", "win-x64", null, "checksum-test", null, null,
                new DotNetPublishPipelineRunner.SourceProvenance(null, null), Array.Empty<string>(), null, null
            }));

            Assert.NotNull(artifact.PublishedChecksumFiles);
            Assert.Contains(Path.Combine(output, "app.exe"), artifact.PublishedChecksumFiles!);
            Assert.Contains(artifact.ServicePackage!.MetadataPath, artifact.PublishedChecksumFiles!);
            Assert.True(File.Exists(Path.Combine(output, "old.dll")));
            Assert.DoesNotContain("old.dll", File.ReadAllText(WriteChecksums(root, artifact)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void StagedChecksums_ExcludeLockedRuntimeFilesAndIncludeGeneratedPackageAndEvidence()
    {
        string root = Directory.CreateTempSubdirectory("PowerForge.Checksums.").FullName;
        try
        {
            string stage = Directory.CreateDirectory(Path.Combine(root, "stage")).FullName;
            string output = Directory.CreateDirectory(Path.Combine(root, "installed")).FullName;
            Directory.CreateDirectory(Path.Combine(stage, "assets"));
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new executable");
            File.WriteAllText(Path.Combine(stage, "assets", "config.json"), "packaged content");
            DotNetPublishPipelineRunner.DirectoryCopy(stage, output);
            string metadata = Path.Combine(output, "ServicePackage.json");
            string install = Path.Combine(output, "Install-Service.ps1");
            Directory.CreateDirectory(Path.Combine(output, "config"));
            string bootstrap = Path.Combine(output, "config", "settings.json");
            File.WriteAllText(metadata, "metadata");
            File.WriteAllText(install, "install script");
            File.WriteAllText(bootstrap, "bootstrap");
            var artifact = new DotNetPublishArtefactResult
            {
                OutputDir = output,
                PublishedChecksumFiles = DotNetPublishPipelineRunner.CaptureStagedPublishChecksumFiles(
                    stage, output, new DotNetPublishServicePackageResult
                    {
                        MetadataPath = metadata,
                        InstallScriptPath = install,
                        ConfigBootstrapFiles = new[] { @"config\settings.json" }
                    })
            };
            // Runtime state can exist before deployment and can also appear after startup.
            string liveLog = Path.Combine(output, "live.log");
            File.WriteAllText(liveLog, "runtime log");
            File.WriteAllText(Path.Combine(output, "state.db"), "database");
            File.WriteAllText(Path.Combine(output, "old.dll"), "stale executable");
            string evidence = Path.Combine(root, "inventory.json");
            File.WriteAllText(evidence, "release evidence");
            artifact.EvidencePaths = new[] { evidence };
            using var locked = new FileStream(liveLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            string[] inputs = DotNetPublishPipelineRunner.EnumerateArtifactFilesForChecksum(artifact).ToArray();
            Assert.Equal(6, inputs.Length);
            Assert.Contains(Path.Combine(output, "assets", "config.json"), inputs);
            Assert.Contains(metadata, inputs);
            Assert.Contains(install, inputs);
            Assert.Contains(bootstrap, inputs);
            Assert.Contains(evidence, inputs);

            string checksums = WriteChecksums(root, artifact);
            string catalog = File.ReadAllText(checksums);
            Assert.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "app.exe")))), catalog, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("app.exe", catalog);
            Assert.DoesNotContain("live.log", catalog);
            Assert.DoesNotContain("state.db", catalog);
            Assert.DoesNotContain("old.dll", catalog);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StagedChecksums_FailWhenDeployedPayloadIsMissing()
    {
        string root = Directory.CreateTempSubdirectory("PowerForge.Checksums.").FullName;
        try
        {
            var artifact = new DotNetPublishArtefactResult
            {
                OutputDir = root,
                PublishedChecksumFiles = new[] { Path.Combine(root, "missing.exe") }
            };
            Assert.Throws<FileNotFoundException>(() => WriteChecksums(root, artifact));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StagedChecksums_FailWhenStagingInventoryIsUnavailable()
    {
        string root = Directory.CreateTempSubdirectory("PowerForge.Checksums.").FullName;
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                DotNetPublishPipelineRunner.CaptureStagedPublishChecksumFiles(Path.Combine(root, "missing"), root, null));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WriteChecksums(string root, DotNetPublishArtefactResult artifact)
    {
        string path = Path.Combine(root, "SHA256SUMS");
        DotNetPublishPipelineRunner.WriteManifestsWithProvenance(
            new DotNetPublishPlan { ProjectRoot = root, Outputs = new DotNetPublishOutputs { ChecksumsPath = path } },
            new List<DotNetPublishArtefactResult> { artifact }, null, null,
            verifiedSourceProvenance: new DotNetPublishPipelineRunner.SourceProvenance(null, null));
        return path;
    }

    private sealed class PayloadProcessRunner(Action<string> captureStaging) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Assert.Equal("publish", request.Arguments[0]);
            int outputIndex = request.Arguments.ToList().IndexOf("--output");
            Assert.True(outputIndex >= 0);
            string output = request.Arguments[outputIndex + 1];
            captureStaging(output);
            File.WriteAllText(Path.Combine(output, "app.exe"), "synthetic publish output");
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }
}
