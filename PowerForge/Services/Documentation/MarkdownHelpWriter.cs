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
        foreach (var cmd in (payload.Commands ?? Enumerable.Empty<DocumentationCommandHelp>())
                     .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var md = RenderCommandMarkdown(moduleName, cmd);
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

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"Module Name: {moduleName}");
        if (!string.IsNullOrWhiteSpace(payload.ModuleGuid))
            sb.AppendLine($"Module Guid: {payload.ModuleGuid}");
        else
            sb.AppendLine("Module Guid: {{ Fill in module Guid }}");
        sb.AppendLine("Download Help Link: {{ Update Download Link }}");
        sb.AppendLine("Help Version: {{ Please enter version of help manually (X.X.X.X) format }}");
        sb.AppendLine("Locale: en-US");
        sb.AppendLine("---");
        sb.AppendLine($"# {moduleName} Module");
        sb.AppendLine("## Description");
        sb.AppendLine(string.IsNullOrWhiteSpace(payload.ModuleDescription) ? "{{ Fill in the Description }}" : payload.ModuleDescription!.Trim());
        sb.AppendLine();
        sb.AppendLine($"## {moduleName} Cmdlets");

        foreach (var cmd in (payload.Commands ?? Enumerable.Empty<DocumentationCommandHelp>())
                     .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var cmdFilePath = Path.Combine(Path.GetFullPath(docsPath), $"{cmd.Name.Trim()}.md");
            var rel = GetRelativeLink(readmeDir, cmdFilePath);
            sb.AppendLine($"### [{cmd.Name.Trim()}]({rel})");
            sb.AppendLine(string.IsNullOrWhiteSpace(cmd.Synopsis) ? "{{ Fill in the Description }}" : cmd.Synopsis.Trim());
            sb.AppendLine();
        }

        File.WriteAllText(readmePath, sb.ToString(), Utf8NoBom);
    }

    private static string RenderCommandMarkdown(string moduleName, DocumentationCommandHelp cmd)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"external help file: {moduleName}-help.xml");
        sb.AppendLine($"Module Name: {moduleName}");
        sb.AppendLine("online version:");
        sb.AppendLine("schema: 2.0.0");
        sb.AppendLine("---");
        sb.AppendLine($"# {cmd.Name.Trim()}");
        sb.AppendLine("## SYNOPSIS");
        sb.AppendLine(string.IsNullOrWhiteSpace(cmd.Synopsis) ? "{{ Fill in the Synopsis }}" : cmd.Synopsis.Trim());
        sb.AppendLine();

        sb.AppendLine("## SYNTAX");
        var syntax = (cmd.Syntax ?? Enumerable.Empty<DocumentationSyntaxHelp>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Text))
            .ToArray();
        if (syntax.Length == 0)
        {
            sb.AppendLine("```");
            sb.AppendLine(cmd.Name.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else
        {
            foreach (var s in syntax)
            {
                var title = string.IsNullOrWhiteSpace(s.Name) ? "Default" : s.Name.Trim();
                if (s.IsDefault) title += " (Default)";
                sb.AppendLine($"### {title}");
                sb.AppendLine("```");
                sb.AppendLine(s.Text.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## DESCRIPTION");
        var description = string.IsNullOrWhiteSpace(cmd.Description) ? cmd.Synopsis : cmd.Description;
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "{{ Fill in the Description }}" : description.Trim());
        sb.AppendLine();

        sb.AppendLine("## EXAMPLES");
        sb.AppendLine();
        var examples = (cmd.Examples ?? Enumerable.Empty<DocumentationExampleHelp>())
            .Where(e => e is not null)
            .ToArray();
        if (examples.Length == 0)
        {
            sb.AppendLine("### EXAMPLE 1");
            sb.AppendLine("```");
            sb.AppendLine("An example");
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else
        {
            for (var i = 0; i < examples.Length; i++)
            {
                var ex = examples[i];
                sb.AppendLine($"### EXAMPLE {i + 1}");
                sb.AppendLine("```");
                sb.AppendLine(string.IsNullOrWhiteSpace(ex.Code) ? "An example" : ex.Code.Replace("\r\n", "\n").TrimEnd('\n', '\r').Replace("\n", Environment.NewLine));
                sb.AppendLine("```");
                if (!string.IsNullOrWhiteSpace(ex.Remarks))
                {
                    sb.AppendLine();
                    sb.AppendLine(ex.Remarks.Replace("\r\n", "\n").TrimEnd('\n', '\r').Replace("\n", Environment.NewLine));
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## PARAMETERS");
        sb.AppendLine();

        foreach (var p in (cmd.Parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
                     .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)))
        {
            var name = p.Name.Trim();
            sb.AppendLine($"### -{name}");
            sb.AppendLine(string.IsNullOrWhiteSpace(p.Description) ? $"{{{{ Fill {name} Description }}}}" : p.Description.Trim());
            sb.AppendLine();
            sb.AppendLine("```yaml");
            sb.AppendLine($"Type: {p.Type}");
            sb.AppendLine($"Parameter Sets: {FormatParameterSets(p)}");
            sb.AppendLine($"Aliases: {FormatAliases(p)}");
            sb.AppendLine();
            sb.AppendLine($"Required: {Bool(p.Required)}");
            sb.AppendLine($"Position: {p.Position}");
            sb.AppendLine($"Default value: {DefaultValue(p.DefaultValue)}");
            sb.AppendLine($"Accept pipeline input: {PipelineInput(p.PipelineInput)}");
            sb.AppendLine($"Accept wildcard characters: {Bool(p.AcceptWildcardCharacters)}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("### CommonParameters");
        sb.AppendLine("This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).");
        sb.AppendLine();

        sb.AppendLine("## INPUTS");
        sb.AppendLine();
        sb.AppendLine("## OUTPUTS");
        sb.AppendLine();
        sb.AppendLine("## NOTES");
        sb.AppendLine("General notes");
        sb.AppendLine();
        sb.AppendLine("## RELATED LINKS");
        sb.AppendLine();

        return sb.ToString();
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

    private static string DefaultValue(string value)
        => string.IsNullOrWhiteSpace(value) ? "None" : value.Trim();

    private static string PipelineInput(string value)
        => string.IsNullOrWhiteSpace(value) ? "False" : value.Trim();

    private static string Bool(bool value) => value ? "True" : "False";

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
