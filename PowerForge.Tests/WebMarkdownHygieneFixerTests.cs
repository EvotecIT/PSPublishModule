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
}

