using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebCliApiDocsPreflightTests
{
    [Fact]
    public void HandleSubCommand_ApiDocs_FailsOnPreflightWarningsWhenFailOnWarningsIsSet()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--source-map", "PowerForge.Web=https://example.invalid/blob/main/{path}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_AllowsSuppressedPreflightWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--suppress-warning", "PFWEB.APIDOCS.SOURCE",
                "--source-map", "PowerForge.Web=https://example.invalid/blob/main/{path}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "index.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailsWhenNavSurfaceConfiguredWithoutNav()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--nav-surface", "apidocs"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalXml()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static void TryDeleteDirectory(string path)
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
