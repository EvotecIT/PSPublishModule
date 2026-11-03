namespace PowerForge.Tests;

public sealed partial class ModulePipelineInstallSigningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedUnpackedArtefact_FinalizesDeclaredModuleDestination(bool externalModuleDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            WriteMinimalModule(source, "DemoModule", "1.0.0");
            string output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            string retained = Path.Combine(output, "Caller.ps1");
            File.WriteAllText(retained, "caller owned");
            string modules = externalModuleDirectory ? Path.Combine(root, "external") : output;
            var spec = CreateSpec(source, "DemoModule", Path.Combine(root, "install"));
            spec.Install.Enabled = false;
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true, Path = output, DoNotClear = true,
                        RequiredModules = new ArtefactRequiredModulesConfiguration { ModulesPath = modules }
                    }
                }
            }).ToArray();
            var hosted = new RecordingHostedOperations { HonorSigningPatterns = true, MarkSignedFiles = true };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: null, hostedOperations: hosted);
            var result = runner.Run(spec, runner.Plan(spec));

            Assert.Equal(output, Assert.Single(result.ArtefactResults).OutputPath);
            string module = Path.Combine(modules, "DemoModule", "DemoModule.psm1");
            Assert.Contains(module, hosted.SignedFilePaths);
            Assert.Contains("# synthetic delivery signature", File.ReadAllText(module), StringComparison.Ordinal);
            Assert.DoesNotContain(retained, hosted.SignedFilePaths);
            Assert.Equal("caller owned", File.ReadAllText(retained));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
