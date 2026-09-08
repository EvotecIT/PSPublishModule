using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptOutputSafetyTests
{
    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_RejectsOutputRootThatContainsProjectBeforeCleanup(
        ArtefactType artefactType,
        bool useProjectRoot)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeModule";
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "repository-container")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(outputRoot, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ RootModule = 'SafeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
            string marker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = artefactType,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = useProjectRoot ? projectRoot : outputRoot,
                            ArtefactName = "SafeModule.zip"
                        }
                    },
                    projectRoot,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("contains project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_ScriptPackedAllowsProjectOutputWhenCleanupIsDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ RootModule = 'SafeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
            string marker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            ArtefactBuildResult result = new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.ScriptPacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = projectRoot,
                        ArtefactName = "SafeModule.zip",
                        DoNotClear = true
                    }
                },
                projectRoot,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            Assert.Equal(Path.Combine(projectRoot, "SafeModule.zip"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_RejectsDirectoryMappingThatOverlapsProtectedSourceTreesBeforeCleanup(
        bool targetStaging,
        bool targetAncestor)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeMappingModule";
            string projectContainer = Directory.CreateDirectory(Path.Combine(root.FullName, "project-container")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(projectContainer, "project")).FullName;
            string stagingContainer = Directory.CreateDirectory(Path.Combine(root.FullName, "staging-container")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(stagingContainer, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string protectedMarker = Path.Combine(targetStaging ? stagingRoot : projectRoot, "preserve.txt");
            File.WriteAllText(protectedMarker, "preserve-source");
            string source = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
            File.WriteAllText(Path.Combine(source, "asset.txt"), "asset");
            string destination = targetStaging
                ? targetAncestor ? stagingContainer : stagingRoot
                : targetAncestor ? projectContainer : projectRoot;

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DestinationDirectoriesRelative = false;
            segment.Configuration.DirectoryOutput =
                [new ArtefactCopyMapping { Source = source, Destination = destination }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("directory copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(targetStaging ? "staging" : "project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-source", File.ReadAllText(protectedMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_RejectsFileDestinationThatContainsDirectoryDestinationBeforeCleanup(
        ArtefactType artefactType,
        bool sameDestination)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "MappingCollisionModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            string directorySource = Directory.CreateDirectory(Path.Combine(root.FullName, "directory-source")).FullName;
            File.WriteAllText(Path.Combine(directorySource, "asset.txt"), "asset");
            string fileSource = Path.Combine(root.FullName, "file-source.txt");
            File.WriteAllText(fileSource, "file");

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DirectoryOutput =
                [new ArtefactCopyMapping { Source = directorySource, Destination = sameDestination ? "assets" : "assets/icons" }];
            segment.Configuration.FilesOutput =
                [new ArtefactCopyMapping { Source = fileSource, Destination = "assets" }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("file copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("directory copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_RejectsOverlappingFileDestinationsBeforeCleanup(
        ArtefactType artefactType,
        bool sameDestination)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "FileMappingCollisionModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            string firstSource = Path.Combine(root.FullName, "first.txt");
            string secondSource = Path.Combine(root.FullName, "second.txt");
            File.WriteAllText(firstSource, "first");
            File.WriteAllText(secondSource, "second");

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.FilesOutput =
            [
                new ArtefactCopyMapping { Source = firstSource, Destination = "assets" },
                new ArtefactCopyMapping { Source = secondSource, Destination = sameDestination ? "assets" : "assets/notice.txt" }
            ];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("file copy destinations", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    public void Build_RejectsCopyDestinationInsideProjectBeforeMutation(
        ArtefactType artefactType,
        bool directoryMapping)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "ProjectDestinationModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);

            string projectMarker;
            if (directoryMapping)
            {
                string source = Directory.CreateDirectory(Path.Combine(root.FullName, "directory-source")).FullName;
                File.WriteAllText(Path.Combine(source, "replacement.txt"), "replacement");
                string destination = Directory.CreateDirectory(Path.Combine(projectRoot, "assets")).FullName;
                projectMarker = Path.Combine(destination, "preserve.txt");
                File.WriteAllText(projectMarker, "preserve-project");
                segment.Configuration.DestinationDirectoriesRelative = false;
                segment.Configuration.DirectoryOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = destination }];
            }
            else
            {
                string source = Path.Combine(root.FullName, "file-source.txt");
                File.WriteAllText(source, "replacement");
                projectMarker = Path.Combine(projectRoot, "preserve.txt");
                File.WriteAllText(projectMarker, "preserve-project");
                segment.Configuration.DestinationFilesRelative = false;
                segment.Configuration.FilesOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = projectMarker }];
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("inside project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ConfigurationArtefactSegment CreateSegment(string outputRoot, ArtefactType artefactType) =>
        new()
        {
            ArtefactType = artefactType,
            Configuration = new ArtefactConfiguration
            {
                Enabled = true,
                Path = outputRoot,
                ArtefactName = "SafeModule.zip"
            }
        };

    private static ArtefactBuildResult Build(
        ConfigurationArtefactSegment segment,
        string projectRoot,
        string stagingRoot,
        string moduleName) =>
        new ArtefactBuilder(new NullLogger()).Build(
            segment,
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            Array.Empty<RequiredModuleReference>());

    private static void WriteModule(string stagingRoot, string moduleName)
    {
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psd1"),
            $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0' }}");
        File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
    }
}
