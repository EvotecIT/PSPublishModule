using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal static class CommandHelpMarkdownFormatter
{
    public static string Render(string moduleName, CommandHelpModel model, ExamplesLayout examplesLayout)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        var command = ToDocumentationCommand(model);
        return MarkdownHelpWriter.RenderCommandMarkdown(
            moduleName,
            command,
            onlineVersion: null,
            examplesLayout: ToDocumentationLayout(examplesLayout));
    }

    private static DocumentationCommandHelp ToDocumentationCommand(CommandHelpModel model)
    {
        var commandName = string.IsNullOrWhiteSpace(model.Name) ? "Command" : model.Name.Trim();
        var command = new DocumentationCommandHelp
        {
            Name = commandName,
            Synopsis = model.Synopsis ?? string.Empty,
            Description = model.Description ?? string.Empty
        };

        foreach (var syntax in model.Syntax)
        {
            command.Syntax.Add(new DocumentationSyntaxHelp
            {
                Name = string.IsNullOrWhiteSpace(syntax.Name) ? "Default" : syntax.Name.Trim(),
                Text = RenderSyntax(commandName, syntax)
            });
        }

        foreach (var parameter in model.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            command.Parameters.Add(new DocumentationParameterHelp
            {
                Name = parameter.Name.Trim(),
                Type = parameter.Type ?? string.Empty,
                Description = parameter.Description ?? string.Empty,
                ParameterSets = FindParameterSets(model.Syntax, parameter.Name).ToList(),
                Aliases = parameter.Aliases ?? new List<string>(),
                Required = parameter.Required ?? false,
                Position = parameter.Position ?? string.Empty,
                DefaultValue = parameter.DefaultValue ?? string.Empty,
                PipelineInput = parameter.PipelineInput ?? string.Empty,
                AcceptWildcardCharacters = parameter.Globbing ?? false
            });
        }

        foreach (var example in model.Examples)
        {
            command.Examples.Add(new DocumentationExampleHelp
            {
                Title = example.Title ?? string.Empty,
                Code = example.Code ?? string.Empty,
                Remarks = example.Remarks ?? string.Empty
            });
        }

        foreach (var input in model.Inputs)
        {
            command.Inputs.Add(new DocumentationTypeHelp
            {
                Name = input.TypeName ?? string.Empty,
                Description = input.Description ?? string.Empty
            });
        }

        foreach (var output in model.Outputs)
        {
            command.Outputs.Add(new DocumentationTypeHelp
            {
                Name = output.TypeName ?? string.Empty,
                Description = output.Description ?? string.Empty
            });
        }

        foreach (var link in model.RelatedLinks)
        {
            command.RelatedLinks.Add(new DocumentationLinkHelp
            {
                Text = link.Title ?? string.Empty,
                Uri = link.Uri ?? string.Empty
            });
        }

        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            command.Notes.Add(new DocumentationNoteHelp
            {
                Title = "Notes",
                Text = model.Notes!.Trim()
            });
        }

        return command;
    }

    private static string RenderSyntax(string commandName, SyntaxSet syntax)
    {
        var parts = new List<string>();
        foreach (var parameter in syntax.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            var type = string.IsNullOrWhiteSpace(parameter.Type) ? string.Empty : $" <{parameter.Type.Trim()}>";
            var token = $"-{parameter.Name.Trim()}{type}";
            parts.Add(parameter.Required == true ? token : $"[{token}]");
        }

        var name = string.IsNullOrWhiteSpace(syntax.Name) ? commandName : syntax.Name.Trim();
        return parts.Count == 0 ? name : name + " " + string.Join(" ", parts);
    }

    private static IEnumerable<string> FindParameterSets(IEnumerable<SyntaxSet> syntaxSets, string parameterName)
    {
        var sets = syntaxSets
            .Where(s => s.Parameters.Any(p => p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase)))
            .Select(s => string.IsNullOrWhiteSpace(s.Name) ? "(All)" : s.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sets.Length == 0 ? new[] { "(All)" } : sets;
    }

    private static DocumentationExampleLayout ToDocumentationLayout(ExamplesLayout layout)
        => layout switch
        {
            ExamplesLayout.ProseFirst => DocumentationExampleLayout.ProseFirst,
            ExamplesLayout.AllAsCode => DocumentationExampleLayout.AllAsCode,
            _ => DocumentationExampleLayout.MamlDefault
        };
}
