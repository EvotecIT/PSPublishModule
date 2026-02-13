using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebCliXrefMergeTests
{
    [Fact]
    public void HandleSubCommand_XrefMerge_WritesOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-xref-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            File.WriteAllText(first, """{"references":[{"uid":"docs.install","href":"/docs/install/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"System.String","href":"/api/types/system-string.json"}]}""");
            var output = Path.Combine(root, "merged.json");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "xref-merge",
                new[]
                {
                    "--out", output,
                    "--map", first,
                    "--map", second
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(output));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void HandleSubCommand_XrefMerge_FailsWhenReferenceBudgetExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-xref-merge-max-refs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            File.WriteAllText(first, """{"references":[{"uid":"docs.install","href":"/docs/install/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"docs.quickstart","href":"/docs/quickstart/"}]}""");
            var output = Path.Combine(root, "merged.json");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "xref-merge",
                new[]
                {
                    "--out", output,
                    "--map", first,
                    "--map", second,
                    "--max-references", "1",
                    "--fail-on-warnings"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void HandleSubCommand_XrefMerge_FailsWhenReferenceGrowthBudgetExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-xref-merge-growth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = Path.Combine(root, "merged.json");
            File.WriteAllText(output, """{"references":[{"uid":"docs.old","href":"/docs/old/"}]}""");

            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            var third = Path.Combine(root, "third.json");
            File.WriteAllText(first, """{"references":[{"uid":"docs.one","href":"/docs/one/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"docs.two","href":"/docs/two/"}]}""");
            File.WriteAllText(third, """{"references":[{"uid":"docs.three","href":"/docs/three/"}]}""");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "xref-merge",
                new[]
                {
                    "--out", output,
                    "--map", first,
                    "--map", second,
                    "--map", third,
                    "--max-reference-growth-count", "1",
                    "--fail-on-warnings"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
