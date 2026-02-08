using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static ApiDocModel ParseXml(string xmlPath, Assembly? assembly, WebApiDocsOptions options)
    {
        var apiDoc = new ApiDocModel();
        if (!File.Exists(xmlPath))
            return apiDoc;

        using var stream = File.OpenRead(xmlPath);
        XDocument doc;
        try
        {
            doc = LoadXmlSafe(stream);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to parse XML docs: {xmlPath} ({ex.GetType().Name}: {ex.Message})");
            return apiDoc;
        }
        var docElement = doc.Element("doc");
        if (docElement is null) return apiDoc;

        var assemblyElement = docElement.Element("assembly");
        if (assemblyElement is not null)
        {
            apiDoc.AssemblyName = assemblyElement.Element("name")?.Value ?? string.Empty;
        }

        var members = docElement.Element("members");
        if (members is null) return apiDoc;

        var memberLookup = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var member in members.Elements("member"))
        {
            var memberName = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(memberName))
                continue;
            if (!memberLookup.ContainsKey(memberName))
                memberLookup[memberName] = member;
        }

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;

            var prefix = name[0];
            var fullName = name.Substring(2);

            switch (prefix)
            {
                case 'T':
                    var type = ParseType(member, fullName, name, memberLookup);
                    apiDoc.Types[type.FullName] = type;
                    break;
                case 'M':
                    AddMethod(apiDoc, member, fullName, name, assembly, memberLookup);
                    break;
                case 'P':
                    AddProperty(apiDoc, member, fullName, name, memberLookup);
                    break;
                case 'F':
                    AddField(apiDoc, member, fullName, name, memberLookup);
                    break;
                case 'E':
                    AddEvent(apiDoc, member, fullName, name, memberLookup);
                    break;
            }
        }

        return apiDoc;
    }

    private static ApiDocModel ParsePowerShellHelp(string helpPath, List<string> warnings)
    {
        var apiDoc = new ApiDocModel();
        if (string.IsNullOrWhiteSpace(helpPath))
            return apiDoc;

        var resolved = ResolvePowerShellHelpFile(helpPath, warnings);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            return apiDoc;

        var moduleName = Path.GetFileNameWithoutExtension(resolved) ?? string.Empty;
        if (moduleName.EndsWith("-help", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^5];
        apiDoc.AssemblyName = moduleName;

        XDocument doc;
        try
        {
            doc = LoadXmlSafe(resolved);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse PowerShell help: {Path.GetFileName(resolved)} ({ex.GetType().Name}: {ex.Message})");
            return apiDoc;
        }

        var commandNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/command/2004/10");
        var mamlNs = XNamespace.Get("http://schemas.microsoft.com/maml/2004/10");
        var devNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/2004/10");

        foreach (var command in doc.Descendants(commandNs + "command"))
        {
            var details = command.Element(commandNs + "details");
            var name = details?.Element(commandNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = command.Element(mamlNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var summary = GetFirstParagraph(details?.Element(mamlNs + "description"), mamlNs);
            var remarks = JoinParagraphs(command.Element(mamlNs + "description"), mamlNs);
            var returns = JoinReturnValues(command, commandNs, mamlNs);

            var type = new ApiTypeModel
            {
                Name = name!,
                FullName = name!,
                Namespace = moduleName ?? string.Empty,
                Kind = "Cmdlet",
                Slug = Slugify(name!),
                Summary = summary,
                Remarks = remarks
            };

            var syntax = command.Element(commandNs + "syntax");
            if (syntax is not null)
            {
                foreach (var syntaxItem in syntax.Elements(commandNs + "syntaxItem"))
                {
                    var member = new ApiMemberModel
                    {
                        Name = name!,
                        Returns = returns,
                        Kind = "Method"
                    };
                    foreach (var parameter in syntaxItem.Elements(commandNs + "parameter"))
                    {
                        var paramName = parameter.Element(mamlNs + "name")?.Value?.Trim() ?? string.Empty;
                        var paramSummary = JoinParagraphs(parameter.Element(mamlNs + "description"), mamlNs);
                        var paramType = parameter.Element(commandNs + "parameterValue")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(paramType))
                            paramType = parameter.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();

                        member.Parameters.Add(new ApiParameterModel
                        {
                            Name = paramName,
                            Type = paramType,
                            Summary = paramSummary
                        });
                    }
                    type.Methods.Add(member);
                }
            }

            apiDoc.Types[type.FullName] = type;
        }

        return apiDoc;
    }

    private static XDocument LoadXmlSafe(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadXmlSafe(stream);
    }

    private static XDocument LoadXmlSafe(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string? ResolvePowerShellHelpFile(string helpPath, List<string> warnings)
    {
        if (File.Exists(helpPath))
            return helpPath;

        if (!Directory.Exists(helpPath))
            return null;

        var primary = Directory.GetFiles(helpPath, "*-help.xml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondary = primary.Count == 0
            ? Directory.GetFiles(helpPath, "*help.xml", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        var candidates = primary.Count > 0 ? primary : secondary;
        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
            warnings.Add($"Multiple PowerShell help files found, using {Path.GetFileName(candidates[0])}");

        return candidates[0];
    }

    private static string? GetFirstParagraph(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        return parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    }

    private static string? JoinParagraphs(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        var parts = parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string? JoinReturnValues(XElement command, XNamespace commandNs, XNamespace mamlNs)
    {
        var values = command.Element(commandNs + "returnValues");
        if (values is null) return null;
        var parts = values.Elements(commandNs + "returnValue")
            .Select(rv => JoinParagraphs(rv.Element(mamlNs + "description"), mamlNs))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
