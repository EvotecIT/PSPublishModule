using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Validates that generated command Markdown, MAML external help, and exact build exports describe the same command set.
/// </summary>
internal static class DocumentationParityValidator
{
    public static DocumentationParityReport Validate(
        string docsPath,
        string? externalHelpFilePath,
        ExportSet? exports)
    {
        var errors = new List<string>();
        var markdownCommands = ReadMarkdownCommandNames(docsPath, errors);
        var mamlCommands = ReadMamlCommandNames(externalHelpFilePath, errors);
        var expectedCommands = ReadExactExportedCommandNames(exports);

        AddSetDiffErrors(
            errors,
            expectedCommands,
            markdownCommands,
            "exported command",
            "Markdown command page");

        if (mamlCommands is not null)
        {
            AddSetDiffErrors(
                errors,
                markdownCommands,
                mamlCommands,
                "Markdown command page",
                "MAML command entry");

            AddSetDiffErrors(
                errors,
                expectedCommands,
                mamlCommands,
                "exported command",
                "MAML command entry");
        }

        return new DocumentationParityReport(
            succeeded: errors.Count == 0,
            markdownCommandCount: markdownCommands.Length,
            mamlCommandCount: mamlCommands?.Length,
            exactExportedCommandCount: expectedCommands?.Length,
            errors: errors.ToArray());
    }

    private static string[] ReadMarkdownCommandNames(string docsPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(docsPath) || !Directory.Exists(docsPath))
        {
            errors.Add($"Documentation path does not exist: {docsPath}");
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(docsPath, "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "Readme.md", StringComparison.OrdinalIgnoreCase))
            .Where(IsGeneratedCommandMarkdown)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsGeneratedCommandMarkdown(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[2048];
            var read = reader.Read(buffer, 0, buffer.Length);
            var header = new string(buffer, 0, read);
            return header.Contains("external help file:", StringComparison.OrdinalIgnoreCase) &&
                   header.Contains("schema: 2.0.0", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string[]? ReadMamlCommandNames(string? externalHelpFilePath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(externalHelpFilePath))
            return null;

        if (!File.Exists(externalHelpFilePath))
        {
            errors.Add($"External help file does not exist: {externalHelpFilePath}");
            return Array.Empty<string>();
        }

        try
        {
            var doc = XDocument.Load(externalHelpFilePath, LoadOptions.None);
            XNamespace commandNs = "http://schemas.microsoft.com/maml/dev/command/2004/10";
            return doc
                .Descendants(commandNs + "command")
                .Select(command => command
                    .Element(commandNs + "details")
                    ?.Element(commandNs + "name")
                    ?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            errors.Add($"External help XML is not well-formed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string[]? ReadExactExportedCommandNames(ExportSet? exports)
    {
        if (exports is null)
            return null;

        var names = (exports.Functions ?? Array.Empty<string>())
            .Concat(exports.Cmdlets ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        names = names
            .Where(static name => !ContainsWildcard(name))
            .ToArray();

        if (names.Length == 0)
            return null;

        return names;
    }

    private static bool ContainsWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

    private static void AddSetDiffErrors(
        List<string> errors,
        string[]? expected,
        string[] actual,
        string expectedLabel,
        string actualLabel)
    {
        if (expected is null)
            return;

        var missing = expected
            .Except(actual, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
            errors.Add($"{actualLabel} is missing {missing.Length} {expectedLabel}(s): {string.Join(", ", missing)}");

        var stale = actual
            .Except(expected, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (stale.Length > 0)
            errors.Add($"{actualLabel} has {stale.Length} stale/non-exported command(s): {string.Join(", ", stale)}");
    }
}

/// <summary>
/// Result of comparing generated documentation command surfaces.
/// </summary>
internal sealed class DocumentationParityReport
{
    public DocumentationParityReport(
        bool succeeded,
        int markdownCommandCount,
        int? mamlCommandCount,
        int? exactExportedCommandCount,
        string[] errors)
    {
        Succeeded = succeeded;
        MarkdownCommandCount = markdownCommandCount;
        MamlCommandCount = mamlCommandCount;
        ExactExportedCommandCount = exactExportedCommandCount;
        Errors = errors ?? Array.Empty<string>();
    }

    public bool Succeeded { get; }

    public int MarkdownCommandCount { get; }

    public int? MamlCommandCount { get; }

    public int? ExactExportedCommandCount { get; }

    public string[] Errors { get; }
}
