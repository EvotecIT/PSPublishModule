using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebVisualStoryStagerTests
{
    [Fact]
    public void Stage_ProducesSelfContainedManifest_WithCompletedOutcome()
    {
        var root = CreateBundle();
        try
        {
            var output = Path.Combine(root, "published");
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = output
            });

            Assert.Equal(3, result.ArtifactCount);
            Assert.True(File.Exists(Path.Combine(output, "visual-story.json")));
            Assert.True(File.Exists(Path.Combine(output, "demo.svg")));
            Assert.True(File.Exists(Path.Combine(output, "demo.png")));
            var staged = WebVisualStoryStager.Load(result.ManifestPath);
            var completed = Assert.Single(staged.Artifacts, a => a.Role == "completed");
            Assert.Equal("png", completed.Format);
            Assert.NotNull(completed.Sha256);
            Assert.True(completed.Bytes > 0);
            Assert.Equal("The chart is visible.", staged.Outcome);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AcceptsUtf8BomAndCanonicalizesArtifactMediaTypes()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(manifest),
                WebJsonForTests.Options)!;
            bundle.Artifacts[0].MediaType = "text/plain";
            File.WriteAllText(
                manifest,
                JsonSerializer.Serialize(bundle, WebJsonForTests.Options),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Equal("image/svg+xml", result.Bundle.Artifacts[0].MediaType);
            Assert.Equal("image/svg+xml", WebVisualStoryStager.Load(result.ManifestPath).Artifacts[0].MediaType);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_ReplacesReadOnlyDestinationArtifacts()
    {
        var root = CreateBundle();
        var output = Path.Combine(root, "published");
        var stagedSvg = Path.Combine(output, "demo.svg");
        try
        {
            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = output
            });
            File.SetAttributes(stagedSvg, File.GetAttributes(stagedSvg) | FileAttributes.ReadOnly);

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = output
            });

            Assert.True(File.Exists(stagedSvg));
            Assert.Equal(3, result.ArtifactCount);
        }
        finally
        {
            if (File.Exists(stagedSvg))
                File.SetAttributes(stagedSvg, File.GetAttributes(stagedSvg) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AllowsNormalizingBundleInPlace_WithoutCopyingArtifactsOntoThemselves()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(source, "story.json"),
                OutputPath = source,
                Overwrite = false
            });

            Assert.Equal(Path.Combine(source, "visual-story.json"), result.ManifestPath);
            Assert.True(File.Exists(Path.Combine(source, "demo.svg")));
            Assert.True(File.Exists(Path.Combine(source, "demo.png")));
            Assert.Equal(3, WebVisualStoryStager.Load(result.ManifestPath).Artifacts.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsExistingOutputDirectoryWithoutOverwrite_BeforeReplacingUndeclaredFiles()
    {
        var root = CreateBundle();
        var output = Path.Combine(root, "published");
        var marker = Path.Combine(output, "keep.txt");
        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(marker, "keep");

            var error = Assert.Throws<IOException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = Path.Combine(root, "source", "story.json"),
                    OutputPath = output,
                    Overwrite = false
                }));

            Assert.Contains("output directory already exists", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(output, "visual-story.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetFileSystemPathComparison_DoesNotRequireWritingToTheTargetDirectory()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-read-only-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalMode = File.GetUnixFileMode(root);
        try
        {
            File.SetUnixFileMode(
                root,
                UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            Assert.Equal(StringComparison.Ordinal, WebVisualStoryStager.GetFileSystemPathComparison(root));
        }
        finally
        {
            File.SetUnixFileMode(root, originalMode);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Stage_RejectsDeclaredIntegrityThatDoesNotMatchSource()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Bytes = 0;
            bundle.Artifacts[0].Sha256 = new string('0', 64);
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("size does not match", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_PreservesNestedPathsAndCanonicalizesRoles()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var nested = Path.Combine(source, "media", "animated");
            Directory.CreateDirectory(nested);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(nested, "demo.svg"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Role = "ANIMATED";
            bundle.Artifacts[0].Path = "./media/animated/demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            var animated = Assert.Single(result.Bundle.Artifacts, artifact => artifact.Role == "animated");
            Assert.Equal("media/animated/demo.svg", animated.Path);
            Assert.True(File.Exists(Path.Combine(root, "published", "media", "animated", "demo.svg")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_NormalizesPortableBackslashArtifactPaths()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var media = Path.Combine(source, "media");
            Directory.CreateDirectory(media);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(media, "demo.svg"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "media\\demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Equal("media/demo.svg", result.Bundle.Artifacts[0].Path);
            Assert.True(File.Exists(Path.Combine(root, "published", "media", "demo.svg")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("svg", "invalid.svg")]
    [InlineData("gif", "invalid.gif")]
    [InlineData("apng", "invalid.apng")]
    [InlineData("png", "invalid.png")]
    [InlineData("html", "invalid.html")]
    [InlineData("text", "invalid.txt")]
    public void Stage_ValidatesContentForEveryDeclaredArtifactFormat(string format, string fileName)
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllBytes(Path.Combine(source, fileName), [0xFF, 0xFE, 0xFD]);
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts = bundle.Artifacts
                .Append(new WebVisualStoryArtifact { Role = "source", Format = format, Path = fileName })
                .ToArray();
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AppliesCaseOnlyArtifactRenamesExactlyAndDropsUndeclaredFiles()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var sourceDirectory = Path.Combine(source, "media");
            Directory.CreateDirectory(sourceDirectory);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(sourceDirectory, "demo.svg"));
            var sourceManifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(sourceManifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "media/demo.svg";
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var output = Path.Combine(root, "published");
            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });
            File.WriteAllText(Path.Combine(output, "media", "retained.txt"), "retained");

            var sourceTemporary = Path.Combine(source, "source-case-" + Guid.NewGuid().ToString("N"));
            Directory.Move(sourceDirectory, sourceTemporary);
            var renamedSourceDirectory = Path.Combine(source, "Media");
            Directory.Move(sourceTemporary, renamedSourceDirectory);
            var fileTemporary = Path.Combine(renamedSourceDirectory, "file-case-" + Guid.NewGuid().ToString("N"));
            File.Move(Path.Combine(renamedSourceDirectory, "demo.svg"), fileTemporary);
            File.Move(fileTemporary, Path.Combine(renamedSourceDirectory, "Demo.svg"));
            bundle.Artifacts[0].Path = "Media/Demo.svg";
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });

            Assert.Contains(
                Directory.EnumerateDirectories(output),
                path => string.Equals(Path.GetFileName(path), "Media", StringComparison.Ordinal));
            Assert.Contains(
                Directory.EnumerateFiles(Path.Combine(output, "Media")),
                path => string.Equals(Path.GetFileName(path), "Demo.svg", StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(output, "Media", "retained.txt")));
            var staged = WebVisualStoryStager.Load(Path.Combine(output, "visual-story.json"));
            Assert.Contains(staged.Artifacts, artifact => string.Equals(artifact.Path, "Media/Demo.svg", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsMixedCaseArtifactDirectoryCollisions()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var media = Path.Combine(source, "media");
            Directory.CreateDirectory(media);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(media, "demo.svg"));
            File.Move(Path.Combine(source, "demo.png"), Path.Combine(media, "demo.png"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(manifest),
                WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "media/demo.svg";
            bundle.Artifacts[1].Path = "Media/demo.png";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("consistent casing", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsBundlesBeyondTheArtifactCountLimit()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(manifest),
                WebJsonForTests.Options)!;
            var artifacts = bundle.Artifacts.ToList();
            for (var index = artifacts.Count; index <= 64; index++)
            {
                var fileName = $"transcript-{index}.txt";
                File.WriteAllText(Path.Combine(source, fileName), "Story transcript.");
                artifacts.Add(new WebVisualStoryArtifact
                {
                    Role = "transcript",
                    Format = "text",
                    Path = fileName
                });
            }
            bundle.Artifacts = artifacts.ToArray();
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("64-artifact safety limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsBundlesBeyondTheAggregateByteLimit()
    {
        var root = CreateBundle();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = Path.Combine(root, "source", "story.json"),
                    OutputPath = Path.Combine(root, "published"),
                    MaximumTotalArtifactBytes = 1
                }));

            Assert.Contains("aggregate limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RequiresTextTranscriptArtifacts()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            var transcript = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "transcript");
            transcript.Format = "png";
            transcript.Path = "demo.png";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("transcript artifacts must use the text format", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsTranscriptArtifactsThatAreNotValidUtf8()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllBytes(Path.Combine(source, "demo.txt"), new byte[] { 0x52, 0x75, 0x6E, 0xFF });

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = Path.Combine(source, "story.json"),
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("valid UTF-8 text", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactPathThatConflictsWithStagedManifest()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var reservedDirectory = Path.Combine(source, "visual-story.json");
            Directory.CreateDirectory(reservedDirectory);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(reservedDirectory, "demo.svg"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "visual-story.json/demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("reserved staged manifest", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PromoteStagedDirectory_RestoresExistingBundleWhenPromotionFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-story-promotion-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "published");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "visual-story.json"), "previous");
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                WebVisualStoryStager.PromoteStagedDirectory(
                    Path.Combine(root, "missing-stage"),
                    output));

            Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "visual-story.json")));
            Assert.Empty(Directory.GetDirectories(root, "*.pf-story-backup-*"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactOutsideBundle()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "../outside.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("parent traversal", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsParentTraversalEvenWhenItResolvesInsideTheBundle()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(manifest),
                WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "media/../demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("parent traversal", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RequiresExactlyOneCompletedPng()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts = bundle.Artifacts.Where(a => a.Role != "completed").ToArray();
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("completed PNG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactThroughSymbolicLink()
    {
        var root = CreateBundle();
        try
        {
            var outside = Path.Combine(root, "outside.svg");
            File.WriteAllText(outside, "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
            var link = Path.Combine(root, "source", "linked.svg");
            File.CreateSymbolicLink(link, outside);

            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "linked.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsSymbolicLinkOutputRootBeforeRecovery()
    {
        var root = CreateBundle();
        try
        {
            var target = Path.Combine(root, "output-target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "visual-story.json"), "not-json");
            var linkedOutput = Path.Combine(root, "linked-output");
            Directory.CreateSymbolicLink(linkedOutput, target);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = Path.Combine(root, "source", "story.json"),
                    OutputPath = linkedOutput
                }));

            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(linkedOutput));
            Assert.Equal("not-json", File.ReadAllText(Path.Combine(target, "visual-story.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsCompletedArtifactWhoseExtensionDoesNotMatchPng()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            var completed = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "completed");
            completed.Path = "demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() => WebVisualStoryStager.Load(manifest));

            Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsCompletedArtifactWithCorruptPngBytes()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            File.WriteAllText(Path.Combine(root, "source", "demo.png"), "not a PNG");

            var error = Assert.Throws<InvalidOperationException>(() => WebVisualStoryStager.Load(manifest));

            Assert.Contains("decodable PNG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsDeclaredIntegrityBeforeDecodingCorruptArtifacts()
    {
        var root = CreateBundle();
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });
            File.WriteAllText(Path.Combine(root, "published", "demo.png"), "not a PNG");

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Load(result.ManifestPath));

            Assert.Contains("size does not match", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("decodable PNG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

}
