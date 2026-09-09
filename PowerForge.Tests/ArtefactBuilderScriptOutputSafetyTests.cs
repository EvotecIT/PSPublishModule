using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptOutputSafetyTests
{
    [Fact]
    public void Build_RejectsScriptRootThatContainsOutputRootBeforeCleanup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            string artefactContainer = Directory.CreateDirectory(Path.Combine(root.FullName, "releases")).FullName;
            string outputRoot = Directory.CreateDirectory(Path.Combine(artefactContainer, "current")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ RootModule = 'SafeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
            string outputMarker = Path.Combine(outputRoot, "preserve-output.txt");
            string siblingMarker = Path.Combine(artefactContainer, "preserve-sibling.txt");
            File.WriteAllText(outputMarker, "output");
            File.WriteAllText(siblingMarker, "sibling");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Script,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = outputRoot,
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = ".."
                            }
                        }
                    },
                    projectRoot,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("contains output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("output", File.ReadAllText(outputMarker));
            Assert.Equal("sibling", File.ReadAllText(siblingMarker));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RejectsRequiredModuleDestinationOverlappingProjectBeforeMutation(
        bool destinationContainsProject)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "ProjectRequiredModule";
            string projectParent = Directory.CreateDirectory(Path.Combine(root.FullName, "protected-parent")).FullName;
            string projectContainer = Directory.CreateDirectory(Path.Combine(projectParent, "project-container")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(projectContainer, "project")).FullName;
            string buildRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "build")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(buildRoot, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(buildRoot, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string dependencyName = destinationContainsProject ? "project-container" : "Dependency.Tools";
            string requiredRoot = destinationContainsProject ? projectParent : projectRoot;
            if (!destinationContainsProject)
                Directory.CreateDirectory(Path.Combine(projectRoot, dependencyName));
            string projectMarker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(projectMarker, "preserve-project");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.RequiredModules.Enabled = true;
            segment.Configuration.RequiredModules.Path = requiredRoot;
            segment.Configuration.RequiredModules.ModulesPath = "app";

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    projectRoot,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    [new RequiredModuleReference(dependencyName)]));

            Assert.Contains("required module destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RejectsGeneratedScriptRootOverlappingProjectBeforeMutation(
        bool scriptRootContainsProject)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "ProjectScriptRoot";
            string projectParent = Directory.CreateDirectory(Path.Combine(root.FullName, "protected-parent")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(projectParent, "project")).FullName;
            string buildRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "build")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(buildRoot, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(buildRoot, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string projectMarker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(projectMarker, "preserve-project");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.RequiredModules.ModulesPath = scriptRootContainsProject
                ? projectParent
                : Path.Combine(projectRoot, "generated");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("generated script root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RejectsMappingDestinationThroughOutputSymlinkBeforeMutation(
        bool directoryMapping)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedMappingModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string projectAssets = Directory.CreateDirectory(Path.Combine(projectRoot, "assets")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string projectMarker = Path.Combine(projectAssets, "preserve.txt");
            File.WriteAllText(projectMarker, "preserve-project");
            linkPath = Path.Combine(outputRoot, "assets");
            try
            {
                Directory.CreateSymbolicLink(linkPath, projectAssets);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DoNotClear = true;
            if (directoryMapping)
            {
                string source = Directory.CreateDirectory(Path.Combine(root.FullName, "directory-source")).FullName;
                File.WriteAllText(Path.Combine(source, "replacement.txt"), "replacement");
                segment.Configuration.DirectoryOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = "assets/content" }];
            }
            else
            {
                string source = Path.Combine(root.FullName, "file-source.txt");
                File.WriteAllText(source, "replacement");
                segment.Configuration.FilesOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = "assets/config.json" }];
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (Directory.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(linkPath);
                    }
                }
                catch { }
            }
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_RejectsDirectoryMappingSourceReparsePointsBeforeOutputMutation(
        ArtefactType artefactType,
        bool linkIsSourceRoot)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedSourceModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string externalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "external-source")).FullName;
            File.WriteAllText(Path.Combine(externalRoot, "private.txt"), "must-not-publish");

            string sourceRoot;
            if (linkIsSourceRoot)
            {
                sourceRoot = Path.Combine(root.FullName, "linked-source");
                linkPath = sourceRoot;
            }
            else
            {
                sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
                File.WriteAllText(Path.Combine(sourceRoot, "public.txt"), "publish");
                linkPath = Path.Combine(sourceRoot, "linked-content");
            }

            try
            {
                Directory.CreateSymbolicLink(linkPath, externalRoot);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DirectoryOutput =
                [new ArtefactCopyMapping { Source = sourceRoot, Destination = "assets" }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("directory copy source", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (Directory.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(linkPath);
                    }
                }
                catch { }
            }
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RejectsFileMappingSourceSymlinkBeforeOutputMutation(ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedFileSourceModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string externalFile = Path.Combine(root.FullName, "private.txt");
            File.WriteAllText(externalFile, "must-not-publish");
            linkPath = Path.Combine(root.FullName, "linked-file.txt");

            try
            {
                File.CreateSymbolicLink(linkPath, externalFile);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.FilesOutput =
                [new ArtefactCopyMapping { Source = linkPath, Destination = "assets/copied.txt" }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("file copy source", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (File.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        File.Delete(linkPath);
                    }
                }
                catch { }
            }
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_RejectsModulePackageDestinationThroughOutputSymlinkBeforeMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedPackageModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string projectResources = Directory.CreateDirectory(Path.Combine(projectRoot, "resources")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string stagedResources = Directory.CreateDirectory(Path.Combine(stagingRoot, "Resources")).FullName;
            File.WriteAllText(Path.Combine(stagedResources, "config.json"), "replacement");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string projectMarker = Path.Combine(projectResources, "config.json");
            File.WriteAllText(projectMarker, "preserve-project");
            linkPath = Path.Combine(outputRoot, "Resources");
            try
            {
                Directory.CreateSymbolicLink(linkPath, projectResources);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DoNotClear = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("module package destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (Directory.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(linkPath);
                    }
                }
                catch { }
            }
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RejectsOutputRootBelowSymlinkAncestorBeforeCleanup(ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedOutputModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string physicalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "physical-output-parent")).FullName;
            string physicalOutput = Directory.CreateDirectory(Path.Combine(physicalRoot, "workspace", "output")).FullName;
            string marker = Path.Combine(physicalOutput, "preserve.txt");
            File.WriteAllText(marker, "preserve");
            linkPath = Path.Combine(root.FullName, "linked-output-parent");
            try
            {
                Directory.CreateSymbolicLink(linkPath, physicalRoot);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string outputRoot = Path.Combine(linkPath, "workspace", "output");
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(CreateSegment(outputRoot, artefactType), projectRoot, stagingRoot, moduleName));

            Assert.Contains("output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (Directory.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(linkPath);
                    }
                }
                catch { }
            }
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
