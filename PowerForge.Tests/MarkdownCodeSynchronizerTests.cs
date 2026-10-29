using System.Text;
using PowerForge;
using PowerForge.Web.Cli;

public sealed class MarkdownCodeSynchronizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-code-sync-" + Guid.NewGuid().ToString("N"));
    public MarkdownCodeSynchronizerTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Synchronize_RemovedMarkers_RejectsVacuousSuccess()
    {
        Write("README.md", "# Guide\n\n```csharp\nvar number = 1;\n```\n");
        Assert.Throws<InvalidDataException>(() => MarkdownCodeSynchronizer.Synchronize(Spec("README.md")));
    }

    [Fact]
    public void Synchronize_ExplicitLanguageAndMissingFinalNewline_PreservesDocumentShape()
    {
        Write("example.txt", "  const value = 42;\n");
        Write("README.md", "<!-- powerforge:code source=\"example.txt\" language=\"typescript\" -->\nold\n<!-- /powerforge:code -->");
        MarkdownCodeSynchronizer.Synchronize(Spec("README.md"), true);
        Assert.Equal("<!-- powerforge:code source=\"example.txt\" language=\"typescript\" -->\n```typescript\nconst value = 42;\n```\n<!-- /powerforge:code -->",
            File.ReadAllText(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void Synchronize_CheckThenApply_PreservesProseBomAndNewlinesAndIsIdempotent()
    {
        Write("Example.cs", "#region QuickStart\n    var value = 42;\n    #region Detail\n    Print(value);\n    #endregion\n#endregion\n");
        var original = "# Guide\r\n\r\n" + Marker("Example.cs", "QuickStart") + "\r\nold\r\n<!-- /powerforge:code -->\r\n\r\nKeep this prose.\r\n";
        File.WriteAllText(Path.Combine(_root, "README.md"), original, new UTF8Encoding(true));
        var bytes = File.ReadAllBytes(Path.Combine(_root, "README.md"));
        var spec = Spec("README.md");
        var check = MarkdownCodeSynchronizer.Synchronize(spec);
        Assert.Equal(new[] { "README.md" }, check.ChangedFiles);
        Assert.Equal(1, check.BlockCount);
        Assert.False(check.Applied);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "README.md")));

        var applied = MarkdownCodeSynchronizer.Synchronize(spec, apply: true);
        Assert.True(applied.Applied);
        var expected = original.Replace("\r\nold\r\n", "\r\n```csharp\r\nvar value = 42;\r\nPrint(value);\r\n```\r\n");
        Assert.Equal(expected, File.ReadAllText(Path.Combine(_root, "README.md")));
        var updated = File.ReadAllBytes(Path.Combine(_root, "README.md"));
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, updated.Take(3));
        Assert.Empty(MarkdownCodeSynchronizer.Synchronize(spec, apply: true).ChangedFiles);
        Assert.Equal(updated, File.ReadAllBytes(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void Synchronize_WholeFileAndRepeatedSource_UsesSafeFencesAndLeavesQuotedMarkersAlone()
    {
        Write("example.txt", "A\n```\nB\n");
        var quoted = "````markdown\n" + Marker("missing.cs") + "\n<!-- /powerforge:code -->\n````\n";
        Write("README.md", quoted + Block("example.txt") + "\n" + Block("example.txt"));
        var result = MarkdownCodeSynchronizer.Synchronize(Spec("README.md"), true);
        Assert.Equal(2, result.BlockCount);
        var actual = File.ReadAllText(Path.Combine(_root, "README.md"));
        Assert.StartsWith(quoted, actual);
        Assert.Contains("````text\nA\n```\nB\n````", actual);
        Assert.Empty(MarkdownCodeSynchronizer.Synchronize(Spec("README.md")).ChangedFiles);
    }

    [Fact]
    public void Synchronize_InvalidLaterDocument_DoesNotWriteEarlierUpdates()
    {
        Write("example.cs", "var number = 1;\n");
        var first = Block("example.cs");
        Write("first.md", first);
        Write("second.md", Marker("example.cs") + "\nmissing end");
        Assert.Throws<InvalidDataException>(() => MarkdownCodeSynchronizer.Synchronize(Spec("first.md", "second.md"), true));
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, "first.md")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unclosed")]
    public void Read_InvalidRegion_RejectsAmbiguousOrIncompleteSource(string scenario)
    {
        var source = scenario switch
        {
            "duplicate" => "#region Pick\nA\n#endregion\n#region Pick\nB\n#endregion",
            "unclosed" => "#region Pick\nA",
            _ => "#region Other\nA\n#endregion"
        };
        Write("example.cs", source);
        Assert.Throws<InvalidDataException>(() => SourceCodeReader.Read(_root, "example.cs", "Pick"));
    }

    [Theory]
    [InlineData("unknown-attribute")]
    [InlineData("duplicate-attribute")]
    [InlineData("nested-marker")]
    [InlineData("orphan-end")]
    public void Synchronize_InvalidMarker_RejectsWithoutChangingDocument(string scenario)
    {
        Write("example.cs", "var number = 1;");
        var document = scenario switch
        {
            "unknown-attribute" => "<!-- powerforge:code source=\"example.cs\" unknown=\"x\" -->\n<!-- /powerforge:code -->",
            "duplicate-attribute" => "<!-- powerforge:code source=\"example.cs\" source=\"example.cs\" -->\n<!-- /powerforge:code -->",
            "nested-marker" => Marker("example.cs") + "\n" + Block("example.cs"),
            _ => "<!-- /powerforge:code -->"
        };
        Write("README.md", document);
        Assert.Throws<InvalidDataException>(() => MarkdownCodeSynchronizer.Synchronize(Spec("README.md"), true));
        Assert.Equal(document, File.ReadAllText(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void Synchronize_SourceOutsideRoot_IsRejectedWithoutWriting()
    {
        var document = Block("../outside.cs");
        Write("README.md", document);
        Assert.Throws<InvalidDataException>(() => MarkdownCodeSynchronizer.Synchronize(Spec("README.md"), true));
        Assert.Equal(document, File.ReadAllText(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void Read_ExplicitPowerShellRegion_RemovesOnlyCommonIndentation()
    {
        Write("example.ps1", "#region QuickStart\n\tGet-Item .\n\t  Write-Output 'done'\n#endregion\n");
        Assert.Equal("Get-Item .\n  Write-Output 'done'", SourceCodeReader.Read(_root, "example.ps1", "QuickStart"));
    }

    [Fact]
    public void Pipeline_ChangedSourceAfterSuccessfulCachedRun_FailsUntilApplied()
    {
        Write("example.cs", "var number = 1;");
        Write("README.md", Block("example.cs"));
        Write("sync.json", "{\"rootPath\":\".\",\"files\":[\"README.md\"]}");
        Write("pipeline.json", "{\"cache\":true,\"steps\":[{\"task\":\"docs-sync\",\"config\":\"sync.json\"}]}");
        var pipeline = Path.Combine(_root, "pipeline.json");
        Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
        Write("pipeline.json", "{\"cache\":true,\"steps\":[{\"task\":\"docs-sync\",\"config\":\"sync.json\",\"apply\":true}]}");
        Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
        Write("pipeline.json", "{\"cache\":true,\"steps\":[{\"task\":\"docs-sync\",\"config\":\"sync.json\"}]}");
        Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
        Write("example.cs", "var number = 2;");
        Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
        MarkdownCodeSynchronizer.Synchronize(Spec("README.md"), true);
        Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
    }

    private MarkdownCodeSyncSpec Spec(params string[] files) => new() { RootPath = _root, Files = files };
    private void Write(string file, string text) => File.WriteAllText(Path.Combine(_root, file), text, new UTF8Encoding(false));
    private static string Marker(string source, string? region = null) => "<!-- powerforge:code source=\"" + source + "\"" + (region is null ? "" : " region=\"" + region + "\"") + " -->";
    private static string Block(string source) => Marker(source) + "\nstale\n<!-- /powerforge:code -->";
}
