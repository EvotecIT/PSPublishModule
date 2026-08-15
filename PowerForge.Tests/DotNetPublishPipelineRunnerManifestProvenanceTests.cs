using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void WriteManifests_RecordsCommittedSourceRevisionAndDirtyState()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "source.txt"), "committed");
            RunGit(root, "add source.txt");
            RunGit(root, "commit -m \"test source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();

            var output = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Publish", "app")).FullName;
            string signedOutputPath = Path.Combine(output, "app.dll");
            File.WriteAllText(signedOutputPath, "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var versionStatePath = Path.Combine(root, "Artifacts", "Versioning", "app.msi.state.json");
            var stagingPath = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Msi", "staging")).FullName;
            var prepareManifestPath = Path.Combine(root, "Artifacts", "Msi", "prepare.json");
            var runReportPath = Path.Combine(root, "Artifacts", "run-report.json");
            var generatedProjectDirectory = Directory.CreateDirectory(
                Path.Combine(root, "Artifacts", "Msi", "generated")).FullName;
            var generatedProjectPath = Path.Combine(generatedProjectDirectory, "app.wixproj");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            File.WriteAllText(versionStatePath, "{}");
            File.WriteAllText(Path.Combine(stagingPath, "payload.dll"), "payload");
            File.WriteAllText(prepareManifestPath, "{}");
            File.WriteAllText(runReportPath, "{}");
            File.WriteAllText(generatedProjectPath, "<Project />");
            File.WriteAllText(Path.Combine(generatedProjectDirectory, "Product.wxs"), "<Wix />");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs
                {
                    ManifestJsonPath = manifestPath,
                    RunReportPath = runReportPath
                },
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
                {
                    ["app"] = new() { StatePath = versionStatePath }
                },
                Installers =
                [
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app",
                        Authoring = new PowerForgeInstallerDefinition()
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.MsiPrepare,
                        StagingPath = stagingPath,
                        ManifestPath = prepareManifestPath
                    }
                ]
            };
            var artefacts = new List<DotNetPublishArtefactResult>
            {
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    PublishDir = output,
                    OutputDir = output,
                    Files = 1,
                    TotalBytes = 7,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { signedOutputPath }
                }
            };
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new()
                {
                    InstallerId = "app",
                    ProjectPath = generatedProjectPath,
                    GeneratedProject = true,
                    VersionStatePath = versionStatePath
                }
            };

            InvokeWriteManifests(plan, artefacts, msiBuilds);

            using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                Assert.All(document.RootElement.EnumerateArray(), entry =>
                {
                    Assert.Equal(revision, entry.GetProperty("SourceRevision").GetString());
                    Assert.False(entry.GetProperty("SourceDirty").GetBoolean());
                });
                JsonElement publishEntry = document.RootElement.EnumerateArray()
                    .Single(entry => entry.GetProperty("Category").GetString() == "Publish");
                Assert.Equal(1, publishEntry.GetProperty("SignedFiles").GetInt32());
                Assert.Equal(
                    Path.GetRelativePath(root, signedOutputPath).Replace('\\', '/'),
                    Assert.Single(publishEntry.GetProperty("SignedFilePaths").EnumerateArray()).GetString());
            }

            var untrackedSourcePath = Path.Combine(root, "untracked-input.cs");
            File.WriteAllText(untrackedSourcePath, "source input");
            InvokeWriteManifests(plan, artefacts, msiBuilds);

            using var dirtyDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.All(
                dirtyDocument.RootElement.EnumerateArray(),
                entry => Assert.True(entry.GetProperty("SourceDirty").GetBoolean()));

            File.Delete(untrackedSourcePath);
            File.WriteAllText(Path.Combine(root, "source.txt"), "modified");
            InvokeWriteManifests(plan, artefacts, msiBuilds);

            using var trackedDirtyDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.All(
                trackedDirtyDocument.RootElement.EnumerateArray(),
                entry => Assert.True(entry.GetProperty("SourceDirty").GetBoolean()));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteManifests_DoesNotExcludeConfiguredInstallerSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "source.txt"), "committed");
            RunGit(root, "add source.txt");
            RunGit(root, "commit -m \"test source\"");

            var customProjectDirectory = Directory.CreateDirectory(Path.Combine(root, "Installer")).FullName;
            var customProjectPath = Path.Combine(customProjectDirectory, "custom.wixproj");
            File.WriteAllText(customProjectPath, "<Project />");
            File.WriteAllText(Path.Combine(customProjectDirectory, "Product.wxs"), "<Wix />");
            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath },
                Installers =
                [
                    new DotNetPublishInstallerPlan
                    {
                        Id = "custom",
                        InstallerProjectPath = customProjectPath,
                        Authoring = new PowerForgeInstallerDefinition()
                    }
                ]
            };
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new()
                {
                    InstallerId = "custom",
                    ProjectPath = customProjectPath,
                    GeneratedProject = false
                }
            };

            InvokeWriteManifests(plan, new List<DotNetPublishArtefactResult>(), msiBuilds);

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.All(
                document.RootElement.EnumerateArray(),
                entry => Assert.True(entry.GetProperty("SourceDirty").GetBoolean()));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteManifests_ExcludesTrackedGeneratedVersionState()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "source.txt"), "committed");
            var versionStatePath = Path.Combine(root, "Build", "versioning", "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.0\"}");
            RunGit(root, "add source.txt Build/versioning/app.msi.state.json");
            RunGit(root, "commit -m \"test source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();

            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath },
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
                {
                    ["app"] = new() { StatePath = versionStatePath }
                }
            };
            var cleanTrackedGeneratedPaths =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenancePaths(
                    root,
                    new[] { versionStatePath });
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.1\"}");
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new() { InstallerId = "app", VersionStatePath = versionStatePath }
            };

            InvokeWriteManifests(
                plan,
                new List<DotNetPublishArtefactResult>(),
                msiBuilds,
                cleanTrackedGeneratedPaths);

            using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var entry = Assert.Single(document.RootElement.EnumerateArray());
                Assert.Equal(revision, entry.GetProperty("SourceRevision").GetString());
                Assert.False(entry.GetProperty("SourceDirty").GetBoolean());
            }

            File.WriteAllText(Path.Combine(root, "source.txt"), "modified");
            InvokeWriteManifests(
                plan,
                new List<DotNetPublishArtefactResult>(),
                msiBuilds,
                cleanTrackedGeneratedPaths);

            using var dirtyDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var dirtyEntry = Assert.Single(dirtyDocument.RootElement.EnumerateArray());
            Assert.True(dirtyEntry.GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteManifests_DoesNotExcludePreexistingTrackedVersionStateChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var versionStatePath = Path.Combine(root, "Build", "versioning", "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.0\"}");
            RunGit(root, "add Build/versioning/app.msi.state.json");
            RunGit(root, "commit -m \"test source\"");

            File.WriteAllText(versionStatePath, "{\"Version\":\"9.9.9\"}");
            var cleanTrackedGeneratedPaths =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenancePaths(
                    root,
                    new[] { versionStatePath });
            Assert.Empty(cleanTrackedGeneratedPaths);

            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath },
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
                {
                    ["app"] = new() { StatePath = versionStatePath }
                }
            };
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new() { InstallerId = "app", VersionStatePath = versionStatePath }
            };

            InvokeWriteManifests(
                plan,
                new List<DotNetPublishArtefactResult>(),
                msiBuilds,
                cleanTrackedGeneratedPaths);

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var entry = Assert.Single(document.RootElement.EnumerateArray());
            Assert.True(entry.GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteManifests_DoesNotExcludeOtherTrackedGeneratedOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var stagingPath = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Msi", "staging")).FullName;
            var payloadPath = Path.Combine(stagingPath, "payload.dll");
            File.WriteAllText(payloadPath, "committed");
            RunGit(root, "add -f Artifacts/Msi/staging/payload.dll");
            RunGit(root, "commit -m \"test source\"");

            File.WriteAllText(payloadPath, "modified");
            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath },
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.MsiPrepare,
                        StagingPath = stagingPath
                    }
                ]
            };
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new() { InstallerId = "app" }
            };

            InvokeWriteManifests(plan, new List<DotNetPublishArtefactResult>(), msiBuilds);

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var entry = Assert.Single(document.RootElement.EnumerateArray());
            Assert.True(entry.GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void VerifiedMsiVersionStateWrites_RequireTheExactCurrentRunWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var versionStatePath = Path.Combine(root, "Build", "versioning", "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.0\"}");
            RunGit(root, "add Build/versioning/app.msi.state.json");
            RunGit(root, "commit -m \"test source\"");

            var initiallyCleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    root,
                    new[] { versionStatePath });
            var initialState = Assert.Single(initiallyCleanState);

            var hookBytes = Encoding.UTF8.GetBytes(
                "{\"Version\":\"1.0.1\",\"Source\":\"hook\"}");
            File.WriteAllBytes(versionStatePath, hookBytes);

            var writerBytes = Encoding.UTF8.GetBytes(
                "{\"Version\":\"1.0.1\",\"Source\":\"powerforge\"}");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(hookBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));

            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.0\"}");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                initialState.Value,
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Equal(
                new[] { initialState.Key },
                DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                    root,
                    initiallyCleanState,
                    reservationOwner));

            File.AppendAllText(versionStatePath, Environment.NewLine);
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));

            File.WriteAllBytes(versionStatePath, writerBytes);
            var secondWriterBytes = Encoding.UTF8.GetBytes(
                "{\"Version\":\"1.0.2\",\"Source\":\"powerforge\"}");
            File.WriteAllBytes(versionStatePath, secondWriterBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(writerBytes)),
                Convert.ToHexString(SHA256.HashData(secondWriterBytes)));
            Assert.Equal(
                new[] { initialState.Key },
                DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                    root,
                    initiallyCleanState,
                    reservationOwner));

            var thirdWriterBytes = Encoding.UTF8.GetBytes(
                "{\"Version\":\"1.0.3\",\"Source\":\"powerforge\"}");
            File.WriteAllBytes(versionStatePath, thirdWriterBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(hookBytes)),
                Convert.ToHexString(SHA256.HashData(thirdWriterBytes)));
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));

            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            File.WriteAllText(versionStatePath, "{\"Version\":\"1.0.0\"}");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                initialState.Value,
                Convert.ToHexString(SHA256.HashData(writerBytes)));
            File.WriteAllBytes(versionStatePath, hookBytes);
            RunGit(root, "add Build/versioning/app.msi.state.json");
            File.WriteAllBytes(versionStatePath, writerBytes);
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));

            RunGit(root, "reset HEAD -- Build/versioning/app.msi.state.json");
            RunGit(root, "update-index --chmod=+x Build/versioning/app.msi.state.json");
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Run_ReportsInvalidVersionStatePathAsAFailedResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "source.txt"), "committed");
            RunGit(root, "add source.txt");
            RunGit(root, "commit -m \"test source\"");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
                {
                    ["app"] = new() { StatePath = "\0" }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void VerifiedMsiVersionStateWrites_ResolveSubdirectoryProjectFromGitRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "src", "app");
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(projectRoot);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var versionStatePath = Path.Combine(projectRoot, "Build", "versioning", "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            var initialBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.0\"}");
            File.WriteAllBytes(versionStatePath, initialBytes);
            RunGit(root, "add src/app/Build/versioning/app.msi.state.json");
            RunGit(root, "commit -m \"test source\"");

            var initiallyCleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    projectRoot,
                    new[] { versionStatePath });
            var initialState = Assert.Single(initiallyCleanState);
            Assert.Equal(versionStatePath, initialState.Key);

            var writerBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.1\"}");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(initialBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Equal(
                new[] { versionStatePath },
                DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                    projectRoot,
                    initiallyCleanState,
                    reservationOwner));
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PlannedMsiVersionStatePaths_IncludeDeferredPackagingVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var versionStatePath = Path.Combine(root, "Build", "versioning", "app.msi.state.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Installers =
                [
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app",
                        Versioning = new DotNetPublishMsiVersionOptions
                        {
                            Enabled = true,
                            Monotonic = true,
                            ApplyToPublish = false,
                            StatePath = "Build/versioning/{installer}.msi.state.json"
                        }
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.MsiBuild,
                        InstallerId = "app"
                    }
                ]
            };

            Assert.Equal(
                new[] { versionStatePath },
                DotNetPublishPipelineRunner.EnumeratePlannedMsiVersionStatePaths(plan));
            Assert.Empty(plan.MsiVersions);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteManifests_ExcludesOnlyExternalConfigurationAndCanonicalGeneratedProvenance()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string evidenceRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(evidenceRoot);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "tracked");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored-config.json\n");
            RunGit(root, "add tracked.txt .gitignore");
            RunGit(root, "commit -m \"tracked source\"");
            string releaseConfig = Path.Combine(evidenceRoot, ".release.authorized.1.2.3.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
            string publishConfig = Path.Combine(root, "powerforge.dotnetpublish.caller.json");
            File.WriteAllText(releaseConfig, "{}");
            File.WriteAllText(publishConfig, "{}");
            string forgedCheckoutConfig = Path.Combine(root, Path.GetFileName(releaseConfig));
            File.WriteAllText(forgedCheckoutConfig, "{}");
            string moduleDirectory = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
            string generatedJsonProvenance = Path.Combine(
                moduleDirectory,
                PublishedRegistryProvenanceValidator.ModuleProvenanceFileName);
            string generatedSignedProvenance = Path.Combine(
                moduleDirectory,
                PowerForgeModuleSourceAttestationWriter.FileName);
            File.WriteAllText(generatedJsonProvenance, "{}");
            File.WriteAllText(generatedSignedProvenance, "@{}");

            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "app")).FullName;
            string executablePath = Path.Combine(outputDirectory, "app.exe");
            File.WriteAllText(executablePath, "payload");
            string manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            string checksumsPath = Path.Combine(root, "Artifacts", "SHA256SUMS.txt");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                ConfigurationInputPaths = new[] { releaseConfig, publishConfig },
                GeneratedConfigurationInputPaths = new[] { releaseConfig },
                GeneratedConfigurationInputSha256 = new(StringComparer.OrdinalIgnoreCase)
                {
                    [releaseConfig] = AppleNotarizationService.ComputeFileSha256(releaseConfig)
                },
                GeneratedProvenancePaths = new[] { generatedJsonProvenance, generatedSignedProvenance },
                Outputs = new DotNetPublishOutputs
                {
                    ManifestJsonPath = manifestPath,
                    ChecksumsPath = checksumsPath
                }
            };
            var artefacts = new List<DotNetPublishArtefactResult>
            {
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    PublishDir = outputDirectory,
                    OutputDir = outputDirectory,
                    ExePath = executablePath,
                    Files = 1,
                    TotalBytes = 7
                }
            };

            InvokeWriteManifests(plan, artefacts);

            string[] checksumLines = File.ReadAllLines(checksumsPath);
            Assert.Contains(
                checksumLines,
                line => line.EndsWith(
                    "*.release.authorized.1.2.3.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(checksumLines, line => line.Contains("../", StringComparison.Ordinal));
            Assert.Contains(checksumLines, line => line.EndsWith("*powerforge.dotnetpublish.caller.json", StringComparison.Ordinal));
            using (JsonDocument callerDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(callerDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            RunGit(root, "add powerforge.dotnetpublish.caller.json");
            RunGit(root, "commit -m \"tracked caller configuration\"");
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument forgedConfigManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(forgedConfigManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            File.Delete(forgedCheckoutConfig);
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument cleanManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.False(cleanManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            string externalCallerConfig = Path.Combine(evidenceRoot, "caller.external.json");
            File.WriteAllText(externalCallerConfig, "{}");
            plan.ConfigurationInputPaths = new[] { releaseConfig, publishConfig, externalCallerConfig };
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument externalInputManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(externalInputManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
            plan.ConfigurationInputPaths = new[] { releaseConfig, publishConfig };

            string ignoredConfig = Path.Combine(root, "ignored-config.json");
            File.WriteAllText(ignoredConfig, "{}");
            plan.ConfigurationInputPaths = new[] { releaseConfig, publishConfig, ignoredConfig };
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument ignoredInputManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(ignoredInputManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
            File.Delete(ignoredConfig);
            plan.ConfigurationInputPaths = new[] { releaseConfig, publishConfig };

            File.WriteAllText(Path.Combine(root, "untracked-input.txt"), "input");
            InvokeWriteManifests(plan, artefacts);
            using JsonDocument dirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(dirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
            if (Directory.Exists(evidenceRoot))
                Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public void WriteManifests_IgnoredEvaluatedBuildInputMarksSourceDirty()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Remove="Excluded.cs" />
                    <Content Include="payload.custom" />
                    <Content Include="debug.custom" Condition="'$(Configuration)' == 'Debug'" />
                    <Content Include="rid.custom" Condition="'$(RuntimeIdentifier)' == 'win-x64'" />
                    <Content Include="single.custom" Condition="'$(PublishSingleFile)' == 'true'" />
                    <Content Include="property.custom" Condition="'$(CustomFlavor)' == 'Secure'" />
                    <Content Include="environment.custom" Condition="'$(POWERFORGE_TEST_INPUT)' == 'enabled'" />
                    <Content Include="assets/bin/payload.dat" />
                    <Reference Include="Ignored.LocalAssembly">
                      <HintPath>ignored/LocalAssembly.dll</HintPath>
                    </Reference>
                    <EditorConfigFiles Include="analyzer.editorconfig" />
                    <GlobalAnalyzerConfigFiles Include="analyzer.globalconfig" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Generated.cs\nExcluded.cs\npayload.custom\ndebug.custom\nrid.custom\nsingle.custom\nproperty.custom\nenvironment.custom\nanalyzer.editorconfig\nanalyzer.globalconfig\nignored/\nhooks/\nassets/bin/\n.idea/\nnotes.tmp\nbin/\nobj/\nArtifacts/\n");
            RunGit(root, "add Sample.csproj Program.cs .gitignore");
            RunGit(root, "commit -m \"tracked source\"");

            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "app")).FullName;
            string executablePath = Path.Combine(outputDirectory, "app.exe");
            File.WriteAllText(executablePath, "payload");
            string manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "Sample",
                        ProjectPath = projectPath,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
                                Runtime = "win-x64",
                                Style = DotNetPublishStyle.PortableCompat
                            }
                        ]
                    }
                ],
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath }
            };
            plan.MsBuildProperties["CustomFlavor"] = "Secure";
            plan.EnvironmentVariables["POWERFORGE_TEST_INPUT"] = "enabled";
            var artefacts = new List<DotNetPublishArtefactResult>
            {
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "Sample",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    PublishDir = outputDirectory,
                    OutputDir = outputDirectory,
                    ExePath = executablePath,
                    Files = 1,
                    TotalBytes = 7
                }
            };
            File.WriteAllText(Path.Combine(root, "Generated.cs"), "internal static class Injected { }");

            InvokeWriteManifests(plan, artefacts);

            using (JsonDocument dirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(dirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            File.Delete(Path.Combine(root, "Generated.cs"));
            File.WriteAllText(Path.Combine(root, "Excluded.cs"), "this source is excluded from evaluation");
            string ideaDirectory = Directory.CreateDirectory(Path.Combine(root, ".idea")).FullName;
            File.WriteAllText(Path.Combine(ideaDirectory, "workspace.xml"), "<workspace />");
            File.WriteAllText(Path.Combine(root, "notes.tmp"), "developer notes");
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(Path.Combine(root, "obj", "Generated.cs"), "internal static class BuildGenerated { }");
            InvokeWriteManifests(plan, artefacts);
            using JsonDocument cleanManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.False(cleanManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            string nestedBinDirectory = Directory.CreateDirectory(Path.Combine(root, "assets", "bin")).FullName;
            string nestedBinInput = Path.Combine(nestedBinDirectory, "payload.dat");
            File.WriteAllText(nestedBinInput, "explicit build input");
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument nestedBinDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(nestedBinDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
            File.Delete(nestedBinInput);

            foreach (string analyzerInput in new[] { "analyzer.editorconfig", "analyzer.globalconfig" })
            {
                File.WriteAllText(Path.Combine(root, analyzerInput), "is_global = true");
                InvokeWriteManifests(plan, artefacts);
                using JsonDocument analyzerDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                Assert.True(analyzerDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
                File.Delete(Path.Combine(root, analyzerInput));
            }

            string ignoredReferenceDirectory = Directory.CreateDirectory(Path.Combine(root, "ignored")).FullName;
            string ignoredReference = Path.Combine(ignoredReferenceDirectory, "LocalAssembly.dll");
            File.WriteAllText(ignoredReference, "mutable assembly input");
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument referenceDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(referenceDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            artefacts[0].PublishDir = ignoredReferenceDirectory;
            artefacts[0].OutputDir = ignoredReferenceDirectory;
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument overlappingOutputManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(overlappingOutputManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
            artefacts[0].PublishDir = outputDirectory;
            artefacts[0].OutputDir = outputDirectory;
            File.Delete(ignoredReference);

            string hookDirectory = Directory.CreateDirectory(Path.Combine(root, "hooks")).FullName;
            string hookScript = Path.Combine(hookDirectory, "generate.ps1");
            File.WriteAllText(hookScript, "Set-Content -LiteralPath output.txt -Value generated");
            plan.Steps =
            [
                new DotNetPublishStep
                {
                    Kind = DotNetPublishStepKind.CommandHook,
                    HookId = "generate",
                    HookPhase = DotNetPublishCommandHookPhase.BeforeTargetPublish,
                    HookCommand = "hooks/{hook}.ps1"
                }
            ];
            InvokeWriteManifests(plan, artefacts);
            using (JsonDocument hookDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.True(hookDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
            plan.Steps = Array.Empty<DotNetPublishStep>();
            File.Delete(hookScript);

            File.WriteAllText(Path.Combine(root, "payload.custom"), "published payload");
            InvokeWriteManifests(plan, artefacts);
            using JsonDocument contentDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(contentDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            File.Delete(Path.Combine(root, "payload.custom"));
            plan.Configuration = "Debug";
            File.WriteAllText(Path.Combine(root, "debug.custom"), "debug-only published payload");
            InvokeWriteManifests(plan, artefacts);
            using JsonDocument debugDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(debugDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            File.Delete(Path.Combine(root, "debug.custom"));
            plan.Configuration = "Release";
            foreach (string contextInput in new[] { "rid.custom", "single.custom", "property.custom", "environment.custom" })
            {
                File.WriteAllText(Path.Combine(root, contextInput), "publish-context input");
                InvokeWriteManifests(plan, artefacts);
                using JsonDocument contextDirtyManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                Assert.True(contextDirtyManifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
                File.Delete(Path.Combine(root, contextInput));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsOutsideBuildProjectWhenIgnoredSetIsEmpty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "tracked");
            RunGit(root, "add tracked.txt");
            RunGit(root, "commit -m \"tracked source\"");
            string projectPath = Path.Combine(outside, "Outside.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: new[] { projectPath },
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
            if (Directory.Exists(outside))
            {
                foreach (FileInfo file in new DirectoryInfo(outside).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadPortableInventorySourceProvenance_RejectsCleanRevisionChangedAfterPlanning()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "source.txt"), "first");
            RunGit(root, "add source.txt");
            RunGit(root, "commit -m \"first source\"");
            string plannedRevision = RunGit(root, "rev-parse HEAD").Trim();
            File.WriteAllText(Path.Combine(root, "source.txt"), "second");
            RunGit(root, "add source.txt");
            RunGit(root, "commit -m \"second source\"");
            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "app")).FullName;
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = plannedRevision
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan, outputDirectory));

            Assert.Contains("changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RunBuildInputEvaluationProcess_DrainsBothStreamsAndEnforcesTimeout()
    {
        string fileName;
        IReadOnlyList<string> floodArguments;
        IReadOnlyList<string> timeoutArguments;
        if (OperatingSystem.IsWindows())
        {
            fileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            floodArguments = new[] { "/d", "/s", "/c", "(for /L %i in (1,1,5000) do @echo 012345678901234567890123456789 1>&2) & @echo complete" };
            timeoutArguments = new[] { "/d", "/s", "/c", "ping 127.0.0.1 -n 6 >nul" };
        }
        else
        {
            fileName = "/bin/sh";
            floodArguments = new[] { "-c", "i=0; while [ $i -lt 5000 ]; do echo 012345678901234567890123456789 1>&2; i=$((i+1)); done; echo complete" };
            timeoutArguments = new[] { "-c", "sleep 5" };
        }

        var flood = DotNetPublishPipelineRunner.RunBuildInputEvaluationProcess(
            fileName,
            Path.GetTempPath(),
            floodArguments,
            environmentVariables: null,
            TimeSpan.FromSeconds(20));
        Assert.Equal(0, flood.ExitCode);
        Assert.False(flood.TimedOut);
        Assert.Contains("complete", flood.StdOut, StringComparison.Ordinal);
        Assert.True(flood.StdErr.Length > 65_536);

        var timeout = DotNetPublishPipelineRunner.RunBuildInputEvaluationProcess(
            fileName,
            Path.GetTempPath(),
            timeoutArguments,
            environmentVariables: null,
            TimeSpan.FromMilliseconds(200));
        Assert.True(timeout.TimedOut);
    }

    private static void InvokeWriteManifests(
        DotNetPublishPlan plan,
        List<DotNetPublishArtefactResult> artefacts,
        List<DotNetPublishMsiBuildResult>? msiBuilds = null,
        IEnumerable<string>? cleanTrackedGeneratedPaths = null)
    {
        DotNetPublishPipelineRunner.WriteManifestsWithProvenance(
            plan,
            artefacts,
            new List<DotNetPublishStorePackageResult>(),
            msiBuilds ?? new List<DotNetPublishMsiBuildResult>(),
            cleanTrackedGeneratedPaths);
    }

    [Fact]
    public void ReadSourceProvenance_TrackedReparseInput_IsDirty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config core.symlinks true");
            string linkedFile = Path.Combine(root, "release-link.json");
            string linkedDirectory = Path.Combine(root, "linked-config");
            string externalFile = Path.Combine(externalRoot, "release.json");
            string ignoredDirectory = Path.Combine(root, "ignored-config");
            Directory.CreateDirectory(ignoredDirectory);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored-config/\n");
            File.WriteAllText(externalFile, "approved");
            File.WriteAllText(Path.Combine(ignoredDirectory, "publish.json"), "approved");
            try
            {
                File.CreateSymbolicLink(linkedFile, externalFile);
                Directory.CreateSymbolicLink(linkedDirectory, ignoredDirectory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }
            RunGit(root, "add .");
            RunGit(root, "commit -m \"tracked linked inputs\"");
            if (!RunGit(root, "ls-files -s").Contains("120000", StringComparison.Ordinal))
                return;
            File.WriteAllText(externalFile, "mutated outside checkout");
            File.WriteAllText(Path.Combine(ignoredDirectory, "publish.json"), "mutated under an ignored target");
            Assert.True(string.IsNullOrWhiteSpace(RunGit(root, "status --porcelain=v1")));

            DotNetPublishPipelineRunner.SourceProvenance source =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    explicitInputPaths:
                    [
                        linkedFile,
                        Path.Combine(linkedDirectory, "publish.json")
                    ]);

            Assert.True(source.Dirty);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(externalRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteManifests_ProjectReferenceUsesNearestSelectedTargetFrameworkInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string intermediateRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string childDirectory = Directory.CreateDirectory(Path.Combine(root, "Child")).FullName;
            string parentProject = Path.Combine(root, "Parent.csproj");
            string childProject = Path.Combine(childDirectory, "Child.csproj");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup>" +
                $"<BaseIntermediateOutputPath>{intermediateRoot}\\$(MSBuildProjectName)\\</BaseIntermediateOutputPath>" +
                $"<MSBuildProjectExtensionsPath>{intermediateRoot}\\$(MSBuildProjectName)\\</MSBuildProjectExtensionsPath>" +
                "</PropertyGroup></Project>");
            File.WriteAllText(parentProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks><RuntimeIdentifiers>win-x64</RuntimeIdentifiers></PropertyGroup>" +
                "<ItemGroup><ProjectReference Include=\"Child/Child.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(childProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks><RuntimeIdentifiers>win-x64</RuntimeIdentifiers></PropertyGroup>" +
                "<ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0'\">" +
                "<Content Include=\"net10-only.json\" CopyToOutputDirectory=\"Always\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, ".gitignore"),
                "Child/net10-only.json\nArtifacts/\n");
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"tracked project graph\"");
            File.WriteAllText(Path.Combine(childDirectory, "net10-only.json"), "ignored selected-framework input");
            RunDotNet(root, $"restore \"{parentProject}\" -r win-x64");

            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "app")).FullName;
            string executablePath = Path.Combine(outputDirectory, "app.exe");
            File.WriteAllText(executablePath, "payload");
            string manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var combination = new DotNetPublishTargetCombination
            {
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat
            };
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "Sample",
                        ProjectPath = parentProject,
                        Combinations = new[] { combination }
                    }
                ],
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath }
            };
            var artifact = new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Publish,
                Target = "Sample",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                PublishDir = outputDirectory,
                OutputDir = outputDirectory,
                ExePath = executablePath,
                Files = 1,
                TotalBytes = 7
            };
            var artifacts = new List<DotNetPublishArtefactResult> { artifact };

            Assert.True(
                string.IsNullOrWhiteSpace(RunGit(root, "status --porcelain=v1")),
                RunGit(root, "status --porcelain=v1"));

            InvokeWriteManifests(plan, artifacts);
            using (JsonDocument net8Manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                Assert.False(net8Manifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());

            combination.Framework = "net10.0";
            artifact.Framework = "net10.0";
            InvokeWriteManifests(plan, artifacts);
            using JsonDocument net10Manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(net10Manifest.RootElement[0].GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(intermediateRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void HasReparsePointBelowRoot_DirectoryJunction_IsRejected()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        string junction = Path.Combine(root, "junction-config");
        try
        {
            File.WriteAllText(Path.Combine(externalRoot, "publish.json"), "{}");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("/d");
            process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add("mklink");
            process.StartInfo.ArgumentList.Add("/J");
            process.StartInfo.ArgumentList.Add(junction);
            process.StartInfo.ArgumentList.Add(externalRoot);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"mklink /J failed: {output}{error}");

            Assert.True(DotNetPublishPipelineRunner.HasReparsePointBelowRoot(
                Path.Combine(junction, "publish.json"),
                root));
        }
        finally
        {
            try { Directory.Delete(junction); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(externalRoot, recursive: true); } catch { }
        }
    }

    private static string RunGit(string root, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        string output = process!.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10000), $"git {arguments} timed out");
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {error}");
        return output;
    }

    private static void RunDotNet(string root, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        string output = process!.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120000), $"dotnet {arguments} timed out");
        Assert.True(process.ExitCode == 0, $"dotnet {arguments} failed: {output}{error}");
    }
}
