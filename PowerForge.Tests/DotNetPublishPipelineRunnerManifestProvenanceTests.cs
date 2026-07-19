using System.Diagnostics;
using System.Reflection;
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
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Artifacts/" + Environment.NewLine);
            RunGit(root, "add source.txt .gitignore");
            RunGit(root, "commit -m \"test source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();

            var output = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Publish", "app")).FullName;
            File.WriteAllText(Path.Combine(output, "app.dll"), "payload");
            var manifestPath = Path.Combine(root, "Artifacts", "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath }
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

            InvokeWriteManifests(plan, artefacts);

            using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                JsonElement entry = document.RootElement.EnumerateArray().Single();
                Assert.Equal(revision, entry.GetProperty("SourceRevision").GetString());
                Assert.False(entry.GetProperty("SourceDirty").GetBoolean());
            }

            File.WriteAllText(Path.Combine(root, "source.txt"), "modified");
            File.WriteAllText(Path.Combine(root, "untracked-input.cs"), "source input");
            InvokeWriteManifests(plan, artefacts);

            using var dirtyDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(dirtyDocument.RootElement.EnumerateArray().Single().GetProperty("SourceDirty").GetBoolean());
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
        List<DotNetPublishArtefactResult> artefacts)
    {
        DotNetPublishPipelineRunner.WriteManifestsWithProvenance(
            plan,
            artefacts,
            new List<DotNetPublishStorePackageResult>(),
            new List<DotNetPublishMsiBuildResult>());
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
