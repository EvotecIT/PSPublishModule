using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DocumentationParityValidatorTests
{
    [Fact]
    public void Validate_SucceedsWhenMarkdownMamlAndExactExportsMatch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var docsPath = Path.Combine(root.FullName, "Docs");
            Directory.CreateDirectory(docsPath);
            WriteCommandMarkdown(docsPath, "Get-TestItem");
            var helpPath = Path.Combine(root.FullName, "en-US", "TestModule-help.xml");
            WriteMaml(helpPath, "Get-TestItem");

            var report = DocumentationParityValidator.Validate(
                docsPath,
                helpPath,
                new ExportSet(new[] { "Get-TestItem" }, Array.Empty<string>(), Array.Empty<string>()));

            Assert.True(report.Succeeded);
            Assert.Equal(1, report.MarkdownCommandCount);
            Assert.Equal(1, report.MamlCommandCount);
            Assert.Empty(report.Errors);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Validate_FailsWhenExternalHelpIsMissingMarkdownCommand()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var docsPath = Path.Combine(root.FullName, "Docs");
            Directory.CreateDirectory(docsPath);
            WriteCommandMarkdown(docsPath, "Get-TestItem");
            var helpPath = Path.Combine(root.FullName, "en-US", "TestModule-help.xml");
            WriteMaml(helpPath, "Set-StaleItem");

            var report = DocumentationParityValidator.Validate(
                docsPath,
                helpPath,
                new ExportSet(new[] { "Get-TestItem" }, Array.Empty<string>(), Array.Empty<string>()));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Errors, error => error.Contains("MAML command entry is missing", StringComparison.Ordinal));
            Assert.Contains(report.Errors, error => error.Contains("stale/non-exported", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Validate_SkipsExportComparisonWhenExportsAreWildcarded()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var docsPath = Path.Combine(root.FullName, "Docs");
            Directory.CreateDirectory(docsPath);
            WriteCommandMarkdown(docsPath, "Get-TestItem");

            var report = DocumentationParityValidator.Validate(
                docsPath,
                externalHelpFilePath: null,
                new ExportSet(new[] { "*" }, Array.Empty<string>(), Array.Empty<string>()));

            Assert.True(report.Succeeded);
            Assert.Null(report.ExactExportedCommandCount);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Validate_StillChecksExactCmdletsWhenFunctionsAreWildcarded()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var docsPath = Path.Combine(root.FullName, "Docs");
            Directory.CreateDirectory(docsPath);
            WriteCommandMarkdown(docsPath, "Get-FunctionDoc");

            var report = DocumentationParityValidator.Validate(
                docsPath,
                externalHelpFilePath: null,
                new ExportSet(new[] { "*" }, new[] { "Get-ExactCmdlet" }, Array.Empty<string>()));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Errors, error => error.Contains("Get-ExactCmdlet", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Validate_DoesNotTreatWildcardCoveredCommandsAsStale()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var docsPath = Path.Combine(root.FullName, "Docs");
            Directory.CreateDirectory(docsPath);
            WriteCommandMarkdown(docsPath, "Get-FunctionDoc");
            WriteCommandMarkdown(docsPath, "Get-ExactCmdlet");

            var report = DocumentationParityValidator.Validate(
                docsPath,
                externalHelpFilePath: null,
                new ExportSet(new[] { "*" }, new[] { "Get-ExactCmdlet" }, Array.Empty<string>()));

            Assert.True(report.Succeeded);
            Assert.Equal(1, report.ExactExportedCommandCount);
            Assert.Empty(report.Errors);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteCommandMarkdown(string docsPath, string commandName)
    {
        File.WriteAllText(
            Path.Combine(docsPath, commandName + ".md"),
            "---" + Environment.NewLine +
            "external help file: TestModule-help.xml" + Environment.NewLine +
            "schema: 2.0.0" + Environment.NewLine +
            "---" + Environment.NewLine +
            "# " + commandName + Environment.NewLine);
    }

    private static void WriteMaml(string path, params string[] commandNames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
            "<helpItems xmlns=\"http://msh\" xmlns:command=\"http://schemas.microsoft.com/maml/dev/command/2004/10\">" + Environment.NewLine +
            string.Join(Environment.NewLine, commandNames.Select(commandName =>
                "  <command:command>" + Environment.NewLine +
                "    <command:details>" + Environment.NewLine +
                "      <command:name>" + commandName + "</command:name>" + Environment.NewLine +
                "    </command:details>" + Environment.NewLine +
                "  </command:command>")) + Environment.NewLine +
            "</helpItems>");
    }
}
