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
            File.WriteAllText(Path.Combine(output, "app.dll"), "payload");
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
                    TotalBytes = 7
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
                    initiallyCleanState,
                    reservationOwner));

            File.AppendAllText(versionStatePath, Environment.NewLine);
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
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
}
