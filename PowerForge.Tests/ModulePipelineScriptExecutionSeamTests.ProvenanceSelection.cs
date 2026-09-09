namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Plan_AllowsLeadingAuxiliaryScriptWhenBlankIdGitHubPublishSelectsFirstPackableArtefact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Artefacts/\n");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            var packed = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            spec.Segments = spec.Segments
                .Take(spec.Segments.Length - 1)
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Script,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            ID = "standalone",
                            Path = Path.Combine(root.FullName, "Artefacts", "Script")
                        }
                    },
                    packed
                })
                .ToArray();
            EnableGitHubPublish(spec, moduleName);
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");

            ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true }).Plan(spec);

            Assert.Equal(2, plan.Artefacts.Length);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_AllowsAuxiliaryScriptWhenProvenanceGitHubPublishSelectsPackedArtefact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Artefacts/\n");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            var packed = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            packed.Configuration.ID = "release";
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        ID = "standalone",
                        Path = Path.Combine(root.FullName, "Artefacts", "Script")
                    }
                }
            }).ToArray();
            EnableGitHubPublish(spec, moduleName);
            Assert.IsType<ConfigurationPublishSegment>(spec.Segments.Last()).Configuration.ID = "release";
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");

            ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true }).Plan(spec);

            Assert.Equal(2, plan.Artefacts.Length);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_AllowsAuxiliaryScriptForOuterUnifiedProvenanceReleaseWhenPackedIsFirst()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Artefacts/\n");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root.FullName, "Artefacts", "Script")
                    }
                },
                CreateReleaseProtection(generateProvenance: true)
            }).ToArray();
            spec.UnifiedGitHubRelease = true;
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");

            ModulePipelinePlan plan = CreateRunner(
                new FakeHostedOperations { AutoSuccessfulSigningResult = true }).Plan(spec);

            Assert.Equal(2, plan.Artefacts.Length);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_RejectsFirstScriptForOuterUnifiedProvenanceRelease()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            IConfigurationSegment packed = spec.Segments.Last();
            spec.Segments = spec.Segments
                .Take(spec.Segments.Length - 1)
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Script,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", "Script")
                        }
                    },
                    packed,
                    CreateReleaseProtection(generateProvenance: true)
                })
                .ToArray();
            spec.UnifiedGitHubRelease = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true }).Plan(spec));

            Assert.Contains("does not support Script or ScriptPacked", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
