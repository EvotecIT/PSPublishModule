using PowerForge.Web;

public class WebMarkdownHygieneFixerTests
{
    [Fact]
    public void Fix_DryRun_ReportsChangesWithoutWriting()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-md-fix-dry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "doc.md");
            File.WriteAllText(file,
                """
                <h2>Title</h2>
                <p>Hello <strong>world</strong></p>

                ```csharp
                var x = "<strong>keep</strong>";
                ```
                """);

            var result = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
            {
                RootPath = root,
                ApplyChanges = false
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.FileCount);
            Assert.Equal(1, result.ChangedFileCount);
            Assert.True(result.ReplacementCount >= 2);

            var unchanged = File.ReadAllText(file);
            Assert.Contains("<h2>Title</h2>", unchanged, StringComparison.Ordinal);
            Assert.Contains("<strong>keep</strong>", unchanged, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Fix_ExplicitFileOutsideRoot_IsSkipped()
    {
        var baseRoot = Path.Combine(Path.GetTempPath(), "pf-md-fix-root-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(baseRoot, "root");
        var sibling = Path.Combine(baseRoot, "root2");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        try
        {
            var outsideFile = Path.Combine(sibling, "outside.md");
            File.WriteAllText(outsideFile, "<h1>Outside</h1>");

            var result = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
            {
                RootPath = root,
                Files = new[] { outsideFile },
                ApplyChanges = false
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.FileCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("outside root", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(baseRoot))
                Directory.Delete(baseRoot, true);
        }
    }

    [Fact]
    public void Fix_Apply_WritesConvertedMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-md-fix-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "doc.md");
            File.WriteAllText(file,
                """
                <h3>Header</h3>
                <p>Use <em>markdown</em> and <strong>bold</strong>.</p>
                """);

            var result = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
            {
                RootPath = root,
                ApplyChanges = true
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.ChangedFileCount);
            var updated = File.ReadAllText(file);
            Assert.Contains("### Header", updated, StringComparison.Ordinal);
            Assert.Contains("*markdown*", updated, StringComparison.Ordinal);
            Assert.Contains("**bold**", updated, StringComparison.Ordinal);
            Assert.DoesNotContain("<h3>", updated, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Fix_Apply_NormalizesMultilineMediaTags_AndKeepsCodeFenceContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-md-fix-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "media.md");
            File.WriteAllText(file,
                """
                # Media

                <iframe
                  src="https://example.test/embed"
                  loading="lazy"
                  title="Demo"></iframe>

                ```html
                <iframe
                  src="https://example.test/keep-raw"
                  loading="lazy"></iframe>
                ```
                """);

            var result = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
            {
                RootPath = root,
                ApplyChanges = true
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.ChangedFileCount);
            Assert.True(result.ReplacementCount >= 1);

            var updated = File.ReadAllText(file);
            Assert.Contains("<iframe src=\"https://example.test/embed\" loading=\"lazy\" title=\"Demo\"></iframe>", updated, StringComparison.Ordinal);
            Assert.Contains(
                """
                <iframe
                  src="https://example.test/keep-raw"
                  loading="lazy"></iframe>
                """,
                updated,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
