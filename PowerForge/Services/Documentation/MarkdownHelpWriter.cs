using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Writes markdown help files in a PlatyPS-compatible structure.
/// </summary>
internal sealed class MarkdownHelpWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public void WriteCommandHelpFiles(DocumentationExtractionPayload payload, string moduleName, string docsPath)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(docsPath)) throw new ArgumentException("DocsPath is required.", nameof(docsPath));

        Directory.CreateDirectory(docsPath);
        var onlineVersion = !string.IsNullOrWhiteSpace(payload.HelpInfoUri)
            ? payload.HelpInfoUri!.Trim()
            : string.IsNullOrWhiteSpace(payload.ProjectUri) ? null : payload.ProjectUri!.Trim();
        foreach (var cmd in (payload.Commands ?? Enumerable.Empty<DocumentationCommandHelp>())
                     .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var md = RenderCommandMarkdown(moduleName, cmd, onlineVersion);
            var path = Path.Combine(docsPath, $"{cmd.Name.Trim()}.md");
            File.WriteAllText(path, md, Utf8NoBom);
        }
    }

    public void WriteModuleReadme(DocumentationExtractionPayload payload, string moduleName, string readmePath, string docsPath)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(readmePath)) throw new ArgumentException("ReadmePath is required.", nameof(readmePath));
        if (string.IsNullOrWhiteSpace(docsPath)) throw new ArgumentException("DocsPath is required.", nameof(docsPath));

        var readmeDir = Path.GetDirectoryName(Path.GetFullPath(readmePath)) ?? Path.GetFullPath(docsPath);

        var doc = new MarkdownDocumentBuilder(blankLineAfterFrontMatter: false);
        doc.FrontMatterRaw("Module Name", moduleName);
        doc.FrontMatterRaw("Module Guid",
            !string.IsNullOrWhiteSpace(payload.ModuleGuid)
                ? payload.ModuleGuid!.Trim()
                : "{{ Fill in module Guid }}");
        var downloadLink = !string.IsNullOrWhiteSpace(payload.HelpInfoUri)
            ? payload.HelpInfoUri!.Trim()
            : string.IsNullOrWhiteSpace(payload.ProjectUri) ? "{{ Update Download Link }}" : payload.ProjectUri!.Trim();
        doc.FrontMatterRaw("Download Help Link", downloadLink);
        var helpVersion = string.IsNullOrWhiteSpace(payload.ModuleVersion)
            ? "{{ Please enter version of help manually (X.X.X.X) format }}"
            : payload.ModuleVersion!.Trim();
        doc.FrontMatterRaw("Help Version", helpVersion);
        doc.FrontMatterRaw("Locale", "en-US");
        doc.RawLine($"# {moduleName} Module");
        doc.RawLine("## Description");
        doc.RawLine(string.IsNullOrWhiteSpace(payload.ModuleDescription) ? "{{ Fill in the Description }}" : payload.ModuleDescription!.Trim());
        doc.BlankLine();
        doc.RawLine($"## {moduleName} Cmdlets");

        foreach (var cmd in (payload.Commands ?? Enumerable.Empty<DocumentationCommandHelp>())
                     .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var cmdFilePath = Path.Combine(Path.GetFullPath(docsPath), $"{cmd.Name.Trim()}.md");
            var rel = GetRelativeLink(readmeDir, cmdFilePath);
            doc.RawLine($"### [{cmd.Name.Trim()}]({rel})");
            doc.RawLine(string.IsNullOrWhiteSpace(cmd.Synopsis) ? "{{ Fill in the Description }}" : cmd.Synopsis.Trim());
            doc.BlankLine();
        }

        try
        {
            var aboutDir = Path.Combine(Path.GetFullPath(docsPath), "About");
            if (Directory.Exists(aboutDir))
            {
                var aboutFiles = Directory.EnumerateFiles(aboutDir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFullPath)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (aboutFiles.Length > 0)
                {
                    doc.RawLine("## About Topics");
                    doc.BlankLine();

                    foreach (var f in aboutFiles)
                    {
                        var rel = GetRelativeLink(readmeDir, f);
                        var title = Path.GetFileNameWithoutExtension(f);
                        doc.RawLine($"### [{title}]({rel})");
                        doc.BlankLine();
                    }
                }
            }
        }
        catch
        {
            // best effort
        }

        File.WriteAllText(readmePath, doc.ToString(), Utf8NoBom);
    }

    private static string RenderCommandMarkdown(string moduleName, DocumentationCommandHelp cmd, string? onlineVersion)
    {
        var doc = new MarkdownDocumentBuilder(blankLineAfterFrontMatter: false);
        doc.FrontMatterRaw("external help file", $"{moduleName}-help.xml");
        doc.FrontMatterRaw("Module Name", moduleName);
        if (string.IsNullOrWhiteSpace(onlineVersion))
            doc.FrontMatterRaw("online version");
        else
            doc.FrontMatterRaw("online version", onlineVersion!.Trim());
        doc.FrontMatterRaw("schema", "2.0.0");
        doc.RawLine($"# {cmd.Name.Trim()}");
        doc.RawLine("## SYNOPSIS");
        doc.RawLine(string.IsNullOrWhiteSpace(cmd.Synopsis) ? "{{ Fill in the Synopsis }}" : cmd.Synopsis.Trim());
        doc.BlankLine();

        doc.RawLine("## SYNTAX");
        var syntax = (cmd.Syntax ?? Enumerable.Empty<DocumentationSyntaxHelp>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Text))
            .ToArray();
        if (syntax.Length == 0)
        {
            doc.CodeFence("powershell", cmd.Name.Trim());
        }
        else
        {
            foreach (var s in syntax)
            {
                var title = string.IsNullOrWhiteSpace(s.Name) ? "Default" : s.Name.Trim();
                if (s.IsDefault) title += " (Default)";
                doc.RawLine($"### {title}");
                doc.CodeFence("powershell", s.Text.TrimEnd());
            }
        }

        doc.RawLine("## DESCRIPTION");
        var description = string.IsNullOrWhiteSpace(cmd.Description) ? cmd.Synopsis : cmd.Description;
        doc.RawLine(string.IsNullOrWhiteSpace(description) ? "{{ Fill in the Description }}" : description.Trim());
        doc.BlankLine();

        doc.RawLine("## EXAMPLES");
        doc.BlankLine();
        var examples = (cmd.Examples ?? Enumerable.Empty<DocumentationExampleHelp>())
            .Where(e => e is not null)
            .ToArray();
        if (examples.Length == 0)
        {
            doc.RawLine("### EXAMPLE 1");
            doc.CodeFence("powershell", cmd.Name.Trim());
        }
        else
        {
            for (var i = 0; i < examples.Length; i++)
            {
                var ex = examples[i];
                doc.RawLine($"### EXAMPLE {i + 1}");
                doc.CodeFence("powershell", RenderMarkdownExampleCode(cmd.Name.Trim(), ex));
                if (!string.IsNullOrWhiteSpace(ex.Remarks))
                {
                    doc.RawLine(ex.Remarks.Replace("\r\n", "\n").TrimEnd('\n', '\r').Replace("\n", Environment.NewLine));
                }
                doc.BlankLine();
            }
        }

        doc.RawLine("## PARAMETERS");
        doc.BlankLine();

        foreach (var p in (cmd.Parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
                     .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)))
        {
            var name = p.Name.Trim();
            doc.RawLine($"### -{name}");
            doc.RawLine(string.IsNullOrWhiteSpace(p.Description) ? $"{{{{ Fill {name} Description }}}}" : p.Description.Trim());
            doc.BlankLine();
            doc.CodeFence("yaml", string.Join(Environment.NewLine, new[]
            {
                $"Type: {p.Type}",
                $"Parameter Sets: {FormatParameterSets(p)}",
                $"Aliases: {FormatAliases(p)}",
                $"Possible values: {FormatPossibleValues(p)}",
                string.Empty,
                $"Required: {Bool(p.Required)}",
                $"Position: {p.Position}",
                $"Default value: {DefaultValue(p.DefaultValue)}",
                $"Accept pipeline input: {PipelineInput(p.PipelineInput)}",
                $"Accept wildcard characters: {Bool(p.AcceptWildcardCharacters)}"
            }));
        }

        doc.RawLine("### CommonParameters");
        doc.RawLine("This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).");
        doc.BlankLine();

        doc.RawLine("## INPUTS");
        doc.BlankLine();
        var inputs = (cmd.Inputs ?? Enumerable.Empty<DocumentationTypeHelp>())
            .Where(t => t is not null && (!string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Description)))
            .ToArray();
        if (inputs.Length == 0) doc.RawLine("- `None`");
        foreach (var i in inputs)
        {
            var name = string.IsNullOrWhiteSpace(i.Name) ? "None" : i.Name.Trim();
            var desc = (i.Description ?? string.Empty).Replace("\r\n", "\n").Replace("\n", " ").Trim();
            doc.RawLine(string.IsNullOrWhiteSpace(desc) ? $"- `{name}`" : $"- `{name}` — {desc}");
        }
        doc.BlankLine();

        doc.RawLine("## OUTPUTS");
        doc.BlankLine();
        var outputs = (cmd.Outputs ?? Enumerable.Empty<DocumentationTypeHelp>())
            .Where(t => t is not null && (!string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Description)))
            .ToArray();
        if (outputs.Length == 0) doc.RawLine("- `None`");
        foreach (var o in outputs)
        {
            var name = string.IsNullOrWhiteSpace(o.Name) ? "None" : o.Name.Trim();
            var desc = (o.Description ?? string.Empty).Replace("\r\n", "\n").Replace("\n", " ").Trim();
            doc.RawLine(string.IsNullOrWhiteSpace(desc) ? $"- `{name}`" : $"- `{name}` — {desc}");
        }
        doc.BlankLine();

        doc.RawLine("## RELATED LINKS");
        doc.BlankLine();
        var links = (cmd.RelatedLinks ?? Enumerable.Empty<DocumentationLinkHelp>())
            .Where(l => l is not null && (!string.IsNullOrWhiteSpace(l.Text) || !string.IsNullOrWhiteSpace(l.Uri)))
            .ToArray();
        if (links.Length == 0) doc.RawLine("- None");
        foreach (var l in links)
        {
            var text = string.IsNullOrWhiteSpace(l.Text) ? l.Uri.Trim() : l.Text.Trim();
            if (string.IsNullOrWhiteSpace(l.Uri))
            {
                doc.RawLine($"- {text}");
            }
            else
            {
                var uri = l.Uri.Trim();
                doc.RawLine($"- [{text}]({uri})");
            }
        }
        doc.BlankLine();

        var notes = (cmd.Notes ?? Enumerable.Empty<DocumentationNoteHelp>())
            .Where(n => n is not null && (!string.IsNullOrWhiteSpace(n.Title) || !string.IsNullOrWhiteSpace(n.Text)))
            .ToArray();
        if (notes.Length > 0)
        {
            doc.RawLine("## NOTES");
            doc.BlankLine();
            foreach (var note in notes)
            {
                var title = string.IsNullOrWhiteSpace(note.Title) ? "Note" : note.Title.Trim();
                var text = (note.Text ?? string.Empty).Replace("\r\n", "\n").Trim();
                doc.RawLine($"### {title}");
                doc.BlankLine();
                if (string.IsNullOrWhiteSpace(text))
                {
                    doc.RawLine("- {{ Fill in the note }}");
                }
                else
                {
                    foreach (var paragraph in text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        doc.RawLine(paragraph.Replace("\n", Environment.NewLine).Trim());
                        doc.BlankLine();
                    }
                }
            }
        }

        return doc.ToString();
    }

    private static string FormatParameterSets(DocumentationParameterHelp p)
    {
        var sets = (p.ParameterSets ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        if (sets.Length == 0) return "(All)";
        if (sets.Length == 1 && sets[0].Equals("(All)", StringComparison.OrdinalIgnoreCase)) return "(All)";
        return string.Join(", ", sets);
    }

    private static string FormatAliases(DocumentationParameterHelp p)
    {
        var aliases = (p.Aliases ?? Enumerable.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();

        return aliases.Length == 0 ? string.Empty : string.Join(", ", aliases);
    }

    private static string FormatPossibleValues(DocumentationParameterHelp p)
    {
        var values = (p.PossibleValues ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? string.Empty : string.Join(", ", values);
    }

    private static string DefaultValue(string value)
        => string.IsNullOrWhiteSpace(value) ? "None" : value.Trim();

    private static string PipelineInput(string value)
        => string.IsNullOrWhiteSpace(value) ? "False" : value.Trim();

    private static string Bool(bool value) => value ? "True" : "False";

    private static string RenderMarkdownExampleCode(string commandName, DocumentationExampleHelp example)
    {
        var code = string.IsNullOrWhiteSpace(example.Code)
            ? commandName
            : example.Code.Replace("\r\n", "\n").TrimEnd('\n', '\r');

        var introduction = (example.Introduction ?? string.Empty)
            .Replace("\r\n", "\n")
            .Trim('\r', '\n');

        if (string.IsNullOrWhiteSpace(introduction))
            return code.Replace("\n", Environment.NewLine);

        if (code.StartsWith(introduction, StringComparison.Ordinal))
            return code.Replace("\n", Environment.NewLine);

        var lines = code.Split('\n');
        if (lines.Length == 0)
            return introduction;

        lines[0] = introduction + lines[0];
        return string.Join(Environment.NewLine, lines);
    }

    private static string GetRelativeLink(string fromDirectory, string toPath)
    {
        try
        {
            var from = new Uri(EnsureTrailingSeparator(Path.GetFullPath(fromDirectory)));
            var to = new Uri(Path.GetFullPath(toPath));
            var relativeUri = from.MakeRelativeUri(to);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(toPath);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return p + Path.DirectorySeparatorChar;
    }
}
