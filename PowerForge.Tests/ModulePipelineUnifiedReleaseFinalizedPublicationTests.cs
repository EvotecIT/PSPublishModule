namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests {
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public void Run_DeferredPublicationPreservesFinalizedModuleAndArchive(bool compiled, int mutation) {
        var root = Directory.CreateTempSubdirectory("pf-finalized-publication-");
        try {
            const string name = "TestModule";
            string source = Path.Combine(root.FullName, "source");
            string staging = Path.Combine(root.FullName, "staging");
            WriteMinimalModule(source, name, "1.2.3");
            foreach (string runtime in compiled ? Array.Empty<string>() : new[] { "Core", "Default" }) {
                string lib = Directory.CreateDirectory(Path.Combine(source, "Lib", runtime)).FullName;
                File.Copy(typeof(ModuleBuildPipeline).Assembly.Location, Path.Combine(lib, "Sample.Binary.dll"));
            }
            Dictionary<string, byte[]>? finalized = null;
            var events = new List<string>();
            Dictionary<string, byte[]>? expectedUpload = null;
            int uploadCount = 0;
            int signingCount = 0;
            string? preparedRoot = null;
            var process = new FinalizedPublicationPowerShellRunner(request => {
                string script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("PFPSRG::AVAILABLE::", StringComparison.Ordinal))
                    return new PowerShellRunResult(0, "PFPSRG::AVAILABLE::1", string.Empty, "pwsh.exe");
                Assert.Contains("Publish-PSResource", script, StringComparison.Ordinal);
                Assert.NotNull(expectedUpload);
                Assert.Equal(preparedRoot, request.Arguments[0]);
                var actualFiles = Directory.GetFiles(request.Arguments[0], "*", SearchOption.AllDirectories);
                Assert.Equal(expectedUpload.Keys.OrderBy(path => path),
                    actualFiles.Select(path => Path.GetRelativePath(request.Arguments[0], path)).OrderBy(path => path));
                foreach (var entry in expectedUpload)
                    Assert.Equal(entry.Value, File.ReadAllBytes(Path.Combine(request.Arguments[0], entry.Key)));
                AssertFinalizedBytes();
                uploadCount++;
                return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", string.Empty, "pwsh.exe");
            });
            var hosted = new FakeHostedOperations(events) {
                BinaryDependencyValidation = _ => { },
                RepositoryPublisher = new ModulePublisher(new NullLogger(), process),
                RepositoryPayloadPrepared = upload => {
                    preparedRoot = upload;
                    if (mutation == 2)
                        File.AppendAllText(Path.Combine(upload, name + ".psd1"), "\n# unexpected upload mutation\n");
                    if (mutation == 3)
                        File.WriteAllText(Path.Combine(upload, "unexpected.ps1"), "throw 'unvalidated code'");
                    if (mutation == 4)
                        File.Delete(Path.Combine(upload, name + ".dll"));
                },
                ModuleSigningCompleted = (_, files) => {
                    signingCount++;
                    foreach (string file in files.Where(path => Path.GetExtension(path) is ".ps1" or ".psm1"))
                        File.AppendAllText(file, $"\n# fixture signing pass {signingCount}\n");
                },
                ModulePublishPreflightAction = (_, _) => {
                    if (mutation == 1)
                        File.AppendAllText(Path.Combine(staging, name + ".psm1"), "\n# unexpected mutation before upload\n");
                }
            };
            var runner = CreateRunner(hosted, (_, _, _) => throw new InvalidOperationException("No package build configured."));
            var gate = new GateConfiguration { Mode = ConfigurationGateMode.Build };
            var spec = new ModulePipelineSpec {
                ReleaseCheckpoint = true,
                Build = new ModuleBuildSpec {
                    Name = name, SourcePath = source, Version = "1.2.3",
                    StagingPath = staging, KeepStaging = true, SkipDotNetBuild = !compiled,
                    Frameworks = [], ExportAssemblies = compiled ? [] : ["Sample.Binary.dll"],
                    DisableBinaryCmdletScan = true, UseAssemblyLoadContext = !compiled,
                    PowerShellCompilation = compiled ? new PowerShellModuleCompilationConfiguration {
                        Enabled = true, Mode = PowerShellCompilationMode.Hybrid, TargetFramework = "net10.0",
                        AllowUnreviewedDependencies = true
                    } : null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationGateSegment { Configuration = gate },
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { SignMerged = true } },
                    new ConfigurationOptionsSegment {
                        Options = new ConfigurationOptions {
                            Signing = CreateCompiledSourceCheckpointSigning(root.FullName)
                        }
                    },
                    new ConfigurationArtefactSegment {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration { Enabled = true, Path = Path.Combine(root.FullName, "packed") }
                    },
                    new ConfigurationPublishSegment {
                        Configuration = new PublishConfiguration {
                            Enabled = true, Destination = PublishDestination.PowerShellGallery, ApiKey = "fixture-token",
                            Tool = PublishTool.PSResourceGet, Force = true
                        }
                    }
                ]
            };

            var built = runner.Run(spec);
            string archive = Assert.Single(built.ArtefactResults).OutputPath;
            string[] protectedPaths = compiled
                ? Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Append(archive).ToArray()
                :
            [
                Path.Combine(staging, "Lib", "Core", name + ".ModuleLoadContext.dll"),
                Path.Combine(staging, "Lib", "Default", name + ".ModuleLoadContext.dll"),
                Path.Combine(staging, name + ".Libraries.ps1"),
                Path.Combine(staging, name + ".psm1"), archive
            ];
            Assert.All(protectedPaths, path => Assert.True(File.Exists(path), path));
            finalized = protectedPaths.ToDictionary(path => path, File.ReadAllBytes);
            expectedUpload = ArtefactBuilder.ResolveModulePackageSourceFiles(
                staging, built.Plan.Information, built.Plan.Delivery, true, built.BuildResult!.FinalizedPayloadFiles)
                .ToDictionary(path => Path.GetRelativePath(staging, path), File.ReadAllBytes);
            int finalizedSigningCount = signingCount;
            Assert.True(finalizedSigningCount > 0);
            // Different ZIP timestamps make a repack observable even when generated bytes happen to be deterministic.
            foreach (string path in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(path, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            gate.Mode = ConfigurationGateMode.Publish;
            spec.Build.ReuseStaging = true;
            spec.Build.SkipDotNetBuild = true;

            if (mutation != 0) {
                var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains("changed a release input", exception.Message, StringComparison.Ordinal);
                Assert.Empty(events);
                Assert.Equal(0, uploadCount);
                Assert.Equal(finalizedSigningCount, signingCount);
                if (mutation >= 2) Assert.NotNull(preparedRoot);
                return;
            }

            var published = runner.Run(spec);

            Assert.Equal(new[] { "module:PowerShellGallery" }, events);
            Assert.True(Assert.Single(published.PublishResults).Succeeded);
            Assert.Equal(archive, Assert.Single(published.ArtefactResults).OutputPath);
            AssertFinalizedBytes();
            Assert.Equal(1, uploadCount);
            Assert.Equal(finalizedSigningCount, signingCount);

            void AssertFinalizedBytes() {
                Assert.NotNull(finalized);
                foreach (var entry in finalized)
                    Assert.True(entry.Value.SequenceEqual(File.ReadAllBytes(entry.Key)), "Finalized input changed: " + entry.Key);
            }
        }
        finally { root.Delete(recursive: true); }
    }

    private sealed partial class FakeHostedOperations {
        public ModulePublisher? RepositoryPublisher { get; set; }
        public Action<string>? RepositoryPayloadPrepared { get; set; }
    }

    private sealed class FinalizedPublicationPowerShellRunner(
        Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }
}
