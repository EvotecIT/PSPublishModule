using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Writes external help in MAML XML format (<c>en-US\ModuleName-help.xml</c>).
/// </summary>
internal sealed class MamlHelpWriter
{
    private const string MshNs = "http://msh";
    private const string MamlNs = "http://schemas.microsoft.com/maml/2004/10";
    private const string CommandNs = "http://schemas.microsoft.com/maml/dev/command/2004/10";
    private const string DevNs = "http://schemas.microsoft.com/maml/dev/2004/10";
    private const string MsHelpNs = "http://msdn.microsoft.com/mshelp";

    public string WriteExternalHelpFile(
        DocumentationExtractionPayload payload,
        string moduleName,
        string outputDirectory,
        string? outputFileName = null)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("OutputDirectory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        var fileName = string.IsNullOrWhiteSpace(outputFileName)
            ? $"{moduleName}-help.xml"
            : outputFileName!.Trim();

        var path = Path.Combine(outputDirectory, fileName);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.Entitize
        };

        using var stream = File.Create(path);
        using var writer = XmlWriter.Create(stream, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("helpItems", MshNs);
        writer.WriteAttributeString("schema", "maml");

        foreach (var cmd in (payload.Commands ?? Enumerable.Empty<DocumentationCommandHelp>())
                     .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            WriteCommand(writer, cmd);
        }

        writer.WriteEndElement(); // helpItems
        writer.WriteEndDocument();

        return path;
    }

    private static void WriteCommand(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        var name = cmd.Name.Trim();
        var (verb, noun) = SplitVerbNoun(name);

        writer.WriteStartElement("command", "command", CommandNs);
        writer.WriteAttributeString("xmlns", "maml", null, MamlNs);
        writer.WriteAttributeString("xmlns", "dev", null, DevNs);
        writer.WriteAttributeString("xmlns", "MSHelp", null, MsHelpNs);

        writer.WriteStartElement("command", "details", CommandNs);
        writer.WriteElementString("command", "name", CommandNs, name);
        writer.WriteElementString("command", "verb", CommandNs, verb);
        writer.WriteElementString("command", "noun", CommandNs, noun);
        writer.WriteStartElement("maml", "description", MamlNs);
        WriteParas(writer, Coalesce(cmd.Synopsis, cmd.Description));
        writer.WriteEndElement(); // maml:description
        writer.WriteEndElement(); // command:details

        writer.WriteStartElement("maml", "description", MamlNs);
        WriteParas(writer, string.IsNullOrWhiteSpace(cmd.Description) ? cmd.Synopsis : cmd.Description);
        writer.WriteEndElement(); // maml:description

        WriteSyntax(writer, name, cmd);
        WriteParameters(writer, cmd);
        WriteInputs(writer, cmd);
        WriteOutputs(writer, cmd);

        writer.WriteStartElement("maml", "alertSet", MamlNs);
        writer.WriteStartElement("maml", "alert", MamlNs);
        writer.WriteStartElement("maml", "para", MamlNs);
        writer.WriteString(string.Empty);
        writer.WriteEndElement(); // para
        writer.WriteEndElement(); // alert
        writer.WriteEndElement(); // alertSet

        WriteExamples(writer, cmd);
        WriteRelatedLinks(writer, cmd);

        writer.WriteEndElement(); // command:command
    }

    private static void WriteSyntax(XmlWriter writer, string commandName, DocumentationCommandHelp cmd)
    {
        writer.WriteStartElement("command", "syntax", CommandNs);

        var syntaxSets = (cmd.Syntax ?? Enumerable.Empty<DocumentationSyntaxHelp>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
            .ToArray();

        if (syntaxSets.Length == 0)
        {
            WriteSyntaxItem(writer, commandName, setName: null, cmd.Parameters);
            writer.WriteEndElement(); // syntax
            return;
        }

        foreach (var set in syntaxSets)
        {
            var setName = set.Name.Trim();
            var parameters = (cmd.Parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
                .Where(p => ParameterInSet(p, setName))
                .ToArray();
            WriteSyntaxItem(writer, commandName, setName, parameters);
        }

        writer.WriteEndElement(); // syntax
    }

    private static void WriteSyntaxItem(
        XmlWriter writer,
        string commandName,
        string? setName,
        IEnumerable<DocumentationParameterHelp>? parameters)
    {
        writer.WriteStartElement("command", "syntaxItem", CommandNs);
        if (!string.IsNullOrWhiteSpace(setName))
            writer.WriteAttributeString("parameterSetName", setName.Trim());
        writer.WriteElementString("maml", "name", MamlNs, commandName);

        foreach (var p in (parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
                     .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)))
        {
            WriteParameter(writer, p, includeParameterValue: true);
        }

        writer.WriteEndElement(); // syntaxItem
    }

    private static void WriteParameters(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        writer.WriteStartElement("command", "parameters", CommandNs);

        foreach (var p in (cmd.Parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
                     .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)))
        {
            WriteParameter(writer, p, includeParameterValue: true);
        }

        writer.WriteEndElement(); // parameters
    }

    private static void WriteInputs(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        var inputs = (cmd.Inputs ?? Enumerable.Empty<DocumentationTypeHelp>())
            .Where(t => t is not null && (!string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Description)))
            .ToArray();

        if (inputs.Length == 0)
        {
            writer.WriteStartElement("command", "inputTypes", CommandNs);
            writer.WriteEndElement();
            return;
        }

        writer.WriteStartElement("command", "inputTypes", CommandNs);
        foreach (var t in inputs)
        {
            writer.WriteStartElement("command", "inputType", CommandNs);
            WriteTypeWithDescription(writer, t);
            writer.WriteEndElement(); // inputType
        }
        writer.WriteEndElement(); // inputTypes
    }

    private static void WriteOutputs(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        var outputs = (cmd.Outputs ?? Enumerable.Empty<DocumentationTypeHelp>())
            .Where(t => t is not null && (!string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Description)))
            .ToArray();

        if (outputs.Length == 0)
        {
            writer.WriteStartElement("command", "returnValues", CommandNs);
            writer.WriteEndElement();
            return;
        }

        writer.WriteStartElement("command", "returnValues", CommandNs);
        foreach (var t in outputs)
        {
            writer.WriteStartElement("command", "returnValue", CommandNs);
            WriteTypeWithDescription(writer, t);
            writer.WriteEndElement(); // returnValue
        }
        writer.WriteEndElement(); // returnValues
    }

    private static void WriteExamples(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        var examples = (cmd.Examples ?? Enumerable.Empty<DocumentationExampleHelp>())
            .Where(e => e is not null && (!string.IsNullOrWhiteSpace(e.Code) || !string.IsNullOrWhiteSpace(e.Title) || !string.IsNullOrWhiteSpace(e.Remarks)))
            .ToArray();

        if (examples.Length == 0)
        {
            writer.WriteStartElement("command", "examples", CommandNs);
            writer.WriteEndElement();
            return;
        }

        writer.WriteStartElement("command", "examples", CommandNs);
        for (var i = 0; i < examples.Length; i++)
        {
            var ex = examples[i];
            writer.WriteStartElement("command", "example", CommandNs);
            var title = string.IsNullOrWhiteSpace(ex.Title) ? $"EXAMPLE {i + 1}" : ex.Title.Trim();
            writer.WriteElementString("maml", "title", MamlNs, title);
            writer.WriteElementString("dev", "code", DevNs, ex.Code?.TrimEnd() ?? string.Empty);
            writer.WriteStartElement("dev", "remarks", DevNs);
            WriteParas(writer, ex.Remarks ?? string.Empty);
            writer.WriteEndElement(); // dev:remarks
            writer.WriteEndElement(); // example
        }
        writer.WriteEndElement(); // examples
    }

    private static void WriteRelatedLinks(XmlWriter writer, DocumentationCommandHelp cmd)
    {
        var links = (cmd.RelatedLinks ?? Enumerable.Empty<DocumentationLinkHelp>())
            .Where(l => l is not null && (!string.IsNullOrWhiteSpace(l.Text) || !string.IsNullOrWhiteSpace(l.Uri)))
            .ToArray();

        writer.WriteStartElement("command", "relatedLinks", CommandNs);
        foreach (var link in links)
        {
            writer.WriteStartElement("maml", "navigationLink", MamlNs);
            writer.WriteElementString("maml", "linkText", MamlNs, link.Text?.Trim() ?? string.Empty);
            writer.WriteElementString("maml", "uri", MamlNs, link.Uri?.Trim() ?? string.Empty);
            writer.WriteEndElement(); // navigationLink
        }
        writer.WriteEndElement(); // relatedLinks
    }

    private static void WriteParameter(XmlWriter writer, DocumentationParameterHelp p, bool includeParameterValue)
    {
        var aliases = (p.Aliases ?? Enumerable.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();

        var aliasValue = aliases.Length == 0 ? "none" : string.Join(",", aliases);
        var position = NormalizePosition(p.Position);

        var typeName = string.IsNullOrWhiteSpace(p.Type) ? "Object" : p.Type.Trim();
        var isArray = typeName.EndsWith("[]", StringComparison.Ordinal);

        writer.WriteStartElement("command", "parameter", CommandNs);
        writer.WriteAttributeString("required", p.Required ? "true" : "false");
        writer.WriteAttributeString("variableLength", isArray ? "true" : "false");
        writer.WriteAttributeString("globbing", p.AcceptWildcardCharacters ? "true" : "false");
        writer.WriteAttributeString("pipelineInput", string.IsNullOrWhiteSpace(p.PipelineInput) ? "False" : p.PipelineInput.Trim());
        writer.WriteAttributeString("position", position);
        writer.WriteAttributeString("aliases", aliasValue);

        writer.WriteElementString("maml", "name", MamlNs, p.Name.Trim());
        writer.WriteStartElement("maml", "description", MamlNs);
        WriteParas(writer, p.Description);
        writer.WriteEndElement(); // maml:description

        if (includeParameterValue)
        {
            writer.WriteStartElement("command", "parameterValue", CommandNs);
            writer.WriteAttributeString("required", p.Required ? "true" : "false");
            writer.WriteAttributeString("variableLength", "false");
            writer.WriteString(typeName);
            writer.WriteEndElement(); // parameterValue
        }

        var possibleValues = (p.PossibleValues ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (possibleValues.Length > 0)
        {
            writer.WriteStartElement("command", "parameterValueGroup", CommandNs);
            foreach (var value in possibleValues)
            {
                writer.WriteStartElement("command", "parameterValue", CommandNs);
                writer.WriteAttributeString("required", "false");
                writer.WriteAttributeString("variableLength", "false");
                writer.WriteString(value);
                writer.WriteEndElement(); // parameterValue
            }
            writer.WriteEndElement(); // parameterValueGroup
        }

        writer.WriteStartElement("dev", "type", DevNs);
        writer.WriteElementString("maml", "name", MamlNs, typeName);
        writer.WriteStartElement("maml", "uri", MamlNs);
        writer.WriteEndElement(); // uri
        writer.WriteEndElement(); // dev:type

        var defaultValue = string.IsNullOrWhiteSpace(p.DefaultValue) ? "None" : p.DefaultValue.Trim();
        writer.WriteElementString("dev", "defaultValue", DevNs, defaultValue);

        writer.WriteEndElement(); // command:parameter
    }

    private static void WriteTypeWithDescription(XmlWriter writer, DocumentationTypeHelp type)
    {
        var name = string.IsNullOrWhiteSpace(type.Name) ? "None" : type.Name.Trim();
        writer.WriteStartElement("dev", "type", DevNs);
        writer.WriteElementString("maml", "name", MamlNs, name);
        writer.WriteEndElement(); // dev:type

        if (!string.IsNullOrWhiteSpace(type.Description))
        {
            writer.WriteStartElement("maml", "description", MamlNs);
            WriteParas(writer, type.Description);
            writer.WriteEndElement(); // description
        }
    }

    private static void WriteParas(XmlWriter writer, string? text)
    {
        var paras = SplitParas(text);
        foreach (var para in paras)
        {
            writer.WriteElementString("maml", "para", MamlNs, para);
        }

        if (paras.Length == 0)
        {
            writer.WriteStartElement("maml", "para", MamlNs);
            writer.WriteString(string.Empty);
            writer.WriteEndElement();
        }
    }

    private static string[] SplitParas(string? text)
    {
        if (text is null) return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        return normalized
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace("\n", Environment.NewLine).Trim())     
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
    }

    private static bool ParameterInSet(DocumentationParameterHelp p, string setName)
    {
        if (p is null) return false;
        var sets = p.ParameterSets ?? Enumerable.Empty<string>();
        foreach (var s in sets)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var sn = s.Trim();
            if (sn.Equals("(All)", StringComparison.OrdinalIgnoreCase)) return true;
            if (sn.Equals(setName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizePosition(string? position)
    {
        if (position is null) return "named";
        if (string.IsNullOrWhiteSpace(position)) return "named";
        var p = position.Trim();
        if (p.Equals("Named", StringComparison.OrdinalIgnoreCase)) return "named";
        return p;
    }

    private static (string Verb, string Noun) SplitVerbNoun(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return (string.Empty, string.Empty);
        var idx = commandName.IndexOf('-');
        if (idx <= 0 || idx >= commandName.Length - 1) return (string.Empty, string.Empty);
        return (commandName.Substring(0, idx), commandName.Substring(idx + 1));
    }

    private static string Coalesce(string? primary, string? secondary)    
    {
        if (primary is not null && !string.IsNullOrWhiteSpace(primary)) return primary.Trim();
        return secondary?.Trim() ?? string.Empty;
    }
}
