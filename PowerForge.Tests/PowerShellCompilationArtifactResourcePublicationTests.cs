using PowerForge;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void ArtifactPublisherReplacesAndRemovesOnlyManifestOwnedResourceFiles()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        const string artifactName = "PowerForge.ResourceOwnership";
        try
        {
            Publish("first", includeResource: true);
            File.WriteAllText(Path.Combine(outputDirectory, "Templates", "unrelated.txt"), "unrelated");
            Publish("second", includeResource: true);
            Assert.Equal("second", File.ReadAllText(Path.Combine(outputDirectory, "Templates", "report.txt")));
            Publish("third", includeResource: false);

            Assert.False(File.Exists(Path.Combine(outputDirectory, "Templates", "report.txt")));
            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(outputDirectory, "Templates", "unrelated.txt")));
            Assert.Equal("third", File.ReadAllText(Path.Combine(outputDirectory, artifactName + ".dll")));
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        }

        void Publish(string value, bool includeResource)
        {
            var staging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
            var stagedArtifact = Path.Combine(staging, artifactName + ".dll");
            File.WriteAllText(stagedArtifact, value);
            var durableFiles = new List<object>
            {
                new { path = Path.Combine(outputDirectory, artifactName + ".dll"), sha256 = Hash(stagedArtifact) }
            };
            if (includeResource)
            {
                var resource = Path.Combine(staging, "Templates", "report.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(resource)!);
                File.WriteAllText(resource, value);
                durableFiles.Add(new { path = Path.Combine(outputDirectory, "Templates", "report.txt"), sha256 = Hash(resource) });
            }
            File.WriteAllText(
                Path.Combine(staging, artifactName + ".powerforge-compilation.json"),
                JsonSerializer.Serialize(new { files = durableFiles }));
            PowerShellArtifactSetPublisher.Commit(staging, outputDirectory, artifactName, Array.Empty<string>());
        }
    }

    [Fact]
    public void ArtifactPublisherRejectsCollisionWithUnownedResourceFileAndRestoresPriorArtifact()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "Templates"));
        const string artifactName = "PowerForge.ResourceCollision";
        File.WriteAllText(Path.Combine(outputDirectory, artifactName + ".dll"), "old");
        File.WriteAllText(Path.Combine(outputDirectory, artifactName + ".powerforge-compilation.json"), "{\"files\":[]}");
        File.WriteAllText(Path.Combine(outputDirectory, "Templates", "report.txt"), "unowned");
        var staging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
        Directory.CreateDirectory(Path.Combine(staging, "Templates"));
        File.WriteAllText(Path.Combine(staging, artifactName + ".dll"), "new");
        File.WriteAllText(Path.Combine(staging, artifactName + ".powerforge-compilation.json"), "{\"files\":[]}");
        File.WriteAllText(Path.Combine(staging, "Templates", "report.txt"), "new-resource");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                PowerShellArtifactSetPublisher.Commit(staging, outputDirectory, artifactName, Array.Empty<string>()));

            Assert.Contains("previous durable artifact set was restored", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("old", File.ReadAllText(Path.Combine(outputDirectory, artifactName + ".dll")));
            Assert.Equal("unowned", File.ReadAllText(Path.Combine(outputDirectory, "Templates", "report.txt")));
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ArtifactPublisherSupportsOwnedFileAndDirectoryShapeTransitions()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        const string artifactName = "PowerForge.ResourceShape";
        try
        {
            Publish("Data", "file");
            Assert.Equal("file", File.ReadAllText(Path.Combine(outputDirectory, "Data")));

            Publish(Path.Combine("Data", "item.json"), "directory");
            Assert.False(File.Exists(Path.Combine(outputDirectory, "Data")));
            Assert.Equal("directory", File.ReadAllText(Path.Combine(outputDirectory, "Data", "item.json")));

            Publish("Data", "file-again");
            Assert.False(Directory.Exists(Path.Combine(outputDirectory, "Data")));
            Assert.Equal("file-again", File.ReadAllText(Path.Combine(outputDirectory, "Data")));
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        }

        void Publish(string ownedRelativePath, string content)
        {
            var staging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
            var stagedArtifact = Path.Combine(staging, artifactName + ".dll");
            File.WriteAllText(stagedArtifact, content);
            var stagedOwned = Path.Combine(staging, ownedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedOwned)!);
            File.WriteAllText(stagedOwned, content);
            File.WriteAllText(
                Path.Combine(staging, artifactName + ".powerforge-compilation.json"),
                JsonSerializer.Serialize(new
                {
                    files = new object[]
                    {
                        new { path = Path.Combine(outputDirectory, artifactName + ".dll"), sha256 = Hash(stagedArtifact) },
                        new { path = Path.Combine(outputDirectory, ownedRelativePath), sha256 = Hash(stagedOwned) }
                    }
                }));
            PowerShellArtifactSetPublisher.Commit(staging, outputDirectory, artifactName, Array.Empty<string>());
        }
    }

    [Fact]
    public void ArtifactPublisherRollsBackOwnedFileToDirectoryTransitionAfterLaterCollision()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        const string artifactName = "PowerForge.ResourceShapeRollback";
        try
        {
            var firstStaging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
            var firstArtifact = Path.Combine(firstStaging, artifactName + ".dll");
            var firstData = Path.Combine(firstStaging, "Data");
            File.WriteAllText(firstArtifact, "old-artifact");
            File.WriteAllText(firstData, "old-data");
            File.WriteAllText(
                Path.Combine(firstStaging, artifactName + ".powerforge-compilation.json"),
                JsonSerializer.Serialize(new
                {
                    files = new object[]
                    {
                        new { path = Path.Combine(outputDirectory, artifactName + ".dll"), sha256 = Hash(firstArtifact) },
                        new { path = Path.Combine(outputDirectory, "Data"), sha256 = Hash(firstData) }
                    }
                }));
            PowerShellArtifactSetPublisher.Commit(firstStaging, outputDirectory, artifactName, Array.Empty<string>());

            File.WriteAllText(Path.Combine(outputDirectory, "ZCollision.txt"), "unowned");
            var secondStaging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
            var secondArtifact = Path.Combine(secondStaging, artifactName + ".dll");
            var secondData = Path.Combine(secondStaging, "Data", "item.json");
            File.WriteAllText(secondArtifact, "new-artifact");
            Directory.CreateDirectory(Path.GetDirectoryName(secondData)!);
            File.WriteAllText(secondData, "new-data");
            File.WriteAllText(Path.Combine(secondStaging, "ZCollision.txt"), "collision");
            File.WriteAllText(
                Path.Combine(secondStaging, artifactName + ".powerforge-compilation.json"),
                JsonSerializer.Serialize(new
                {
                    files = new object[]
                    {
                        new { path = Path.Combine(outputDirectory, artifactName + ".dll"), sha256 = Hash(secondArtifact) },
                        new { path = Path.Combine(outputDirectory, "Data", "item.json"), sha256 = Hash(secondData) }
                    }
                }));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                PowerShellArtifactSetPublisher.Commit(secondStaging, outputDirectory, artifactName, Array.Empty<string>()));

            Assert.Contains("previous durable artifact set was restored", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("old-artifact", File.ReadAllText(Path.Combine(outputDirectory, artifactName + ".dll")));
            Assert.Equal("old-data", File.ReadAllText(Path.Combine(outputDirectory, "Data")));
            Assert.Equal("unowned", File.ReadAllText(Path.Combine(outputDirectory, "ZCollision.txt")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "Data", "item.json")));
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        }
    }
}
