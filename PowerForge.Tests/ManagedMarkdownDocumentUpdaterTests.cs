namespace PowerForge.Tests;

public sealed class ManagedMarkdownDocumentUpdaterTests
{
    [Fact]
    public void Update_CreatesManagedDocumentWhenExplicitlyAllowed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "SPONSORS.md");

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "Generated roster",
            CreateIfMissing = true,
            NewDocumentTitle = "Sponsors"
        });

        Assert.True(result.Changed);
        Assert.True(result.Created);
        Assert.False(result.Appended);
        Assert.Equal("# Sponsors\n\n<!-- POWERFORGE:sponsors:START -->\nGenerated roster\n<!-- POWERFORGE:sponsors:END -->\n", Normalize(File.ReadAllText(path)));
    }

    [Fact]
    public void Update_AppendsOnlyWhenExplicitlyConfigured()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "# Project\n\nExisting content.\n");

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "Generated roster",
            MissingBlockBehavior = ManagedMarkdownMissingBlockBehavior.Append
        });

        Assert.True(result.Appended);
        var text = Normalize(File.ReadAllText(path));
        Assert.StartsWith("# Project\n\nExisting content.\n\n", text, StringComparison.Ordinal);
        Assert.Contains("<!-- POWERFORGE:sponsors:START -->", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_RejectsDuplicateMarkersWithoutChangingDocument()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        var original = "<!-- POWERFORGE:sponsors:START -->\none\n<!-- POWERFORGE:sponsors:START -->\ntwo\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(path, original);

        var exception = Assert.Throws<InvalidOperationException>(() => new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "replacement"
        }));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Update_PreservesCrlfAndLegacyBenchmarkMarkers()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "Before\r\n<!-- BENCHMARK:demo:START -->\r\nold\r\n<!-- BENCHMARK:demo:END -->\r\nAfter\r\n");

        var result = new BenchmarkDocumentUpdater().UpdateBlock(path, "demo", "new\nvalue");

        Assert.True(result.Changed);
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\nnew\r\nvalue\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nold\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_PreservesUtf8BomAndEveryByteOutsideMixedEndingBlock()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "Before\r\nKeep\n<!-- POWERFORGE:sponsors:START -->\r\nold\ncontent\r\n<!-- POWERFORGE:sponsors:END -->\nAfter\rTail";
        WriteUtf8Bom(path, original);

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "new\nvalue"
        });

        Assert.True(result.Changed);
        const string expected = "Before\r\nKeep\n<!-- POWERFORGE:sponsors:START -->\r\nnew\r\nvalue\r\n<!-- POWERFORGE:sponsors:END -->\nAfter\rTail";
        var expectedContent = System.Text.Encoding.UTF8.GetBytes(expected);
        var expectedBytes = new byte[3 + expectedContent.Length];
        expectedBytes[0] = 0xEF;
        expectedBytes[1] = 0xBB;
        expectedBytes[2] = 0xBF;
        Buffer.BlockCopy(expectedContent, 0, expectedBytes, 3, expectedContent.Length);
        Assert.Equal(expectedBytes, File.ReadAllBytes(path));
    }

    private static void WriteUtf8Bom(string path, string value)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(value);
        var bytes = new byte[3 + content.Length];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        Buffer.BlockCopy(content, 0, bytes, 3, content.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n');
}
