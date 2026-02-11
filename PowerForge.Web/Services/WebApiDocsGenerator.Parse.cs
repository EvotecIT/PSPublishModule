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
        if (moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^4];
        apiDoc.AssemblyName = moduleName;
        var manifestPath = TryResolvePowerShellModuleManifestPath(resolved, moduleName);
        var kindHints = LoadPowerShellCommandKindHints(manifestPath, warnings);

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
                Kind = ResolvePowerShellCommandKind(name!, kindHints, details?.Element(commandNs + "commandType")?.Value),
                Slug = Slugify(name!),
                Summary = summary,
                Remarks = remarks
            };

            var syntax = command.Element(commandNs + "syntax");
            var commandParameterMap = BuildPowerShellParameterMap(command, commandNs, mamlNs, devNs);
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
                        commandParameterMap.TryGetValue(paramName, out var commandParameter);
                        var paramSummary = JoinParagraphs(parameter.Element(mamlNs + "description"), mamlNs);
                        if (string.IsNullOrWhiteSpace(paramSummary))
                            paramSummary = commandParameter?.Summary;
                        var paramType = parameter.Element(commandNs + "parameterValue")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(paramType))
                            paramType = parameter.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(paramType))
                            paramType = commandParameter?.Type;
                        var isRequired = bool.TryParse(parameter.Attribute("required")?.Value, out var required) && required;
                        if (!isRequired && commandParameter is not null)
                            isRequired = commandParameter.Required;
                        var defaultValue = parameter.Attribute("defaultValue")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(defaultValue))
                            defaultValue = commandParameter?.DefaultValue;

                        member.Parameters.Add(new ApiParameterModel
                        {
                            Name = paramName,
                            Type = paramType,
                            Summary = paramSummary,
                            IsOptional = !isRequired,
                            DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue
                        });
                    }
                    type.Methods.Add(member);
                }
            }

            AppendPowerShellExamples(type, command, commandNs, mamlNs, devNs);

            apiDoc.Types[type.FullName] = type;
        }

        AppendPowerShellAboutTopics(apiDoc, helpPath, resolved, moduleName ?? string.Empty, manifestPath, warnings);
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

    private static Dictionary<string, PowerShellParameterInfo> BuildPowerShellParameterMap(
        XElement command,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs)
    {
        var map = new Dictionary<string, PowerShellParameterInfo>(StringComparer.OrdinalIgnoreCase);
        var parameters = command.Element(commandNs + "parameters");
        if (parameters is null)
            return map;

        foreach (var parameter in parameters.Elements(commandNs + "parameter"))
        {
            var name = parameter.Element(mamlNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var summary = JoinParagraphs(parameter.Element(mamlNs + "description"), mamlNs);
            var type = parameter.Element(commandNs + "parameterValue")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(type))
                type = parameter.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();

            var required = bool.TryParse(parameter.Attribute("required")?.Value, out var parsedRequired) && parsedRequired;
            var defaultValue = parameter.Attribute("defaultValue")?.Value?.Trim();

            map[name] = new PowerShellParameterInfo
            {
                Summary = summary,
                Type = type,
                Required = required,
                DefaultValue = defaultValue
            };
        }

        return map;
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

    private static void AppendPowerShellExamples(
        ApiTypeModel type,
        XElement command,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs)
    {
        if (type is null || command is null)
            return;

        var examples = command.Element(commandNs + "examples");
        if (examples is null)
            return;

        foreach (var example in examples.Elements(commandNs + "example"))
        {
            var narrativeParts = new List<string>();
            var title = NormalizePowerShellExampleTitle(example.Element(mamlNs + "title")?.Value);
            if (!string.IsNullOrWhiteSpace(title))
                narrativeParts.Add(title);

            var introduction = JoinParagraphs(example.Element(mamlNs + "introduction"), mamlNs);
            if (!string.IsNullOrWhiteSpace(introduction))
                narrativeParts.Add(introduction);

            var code = example.Element(devNs + "code")?.Value;
            if (string.IsNullOrWhiteSpace(code))
                code = example.Element(commandNs + "code")?.Value;
            code = code?.Trim();

            var remarks = JoinParagraphs(example.Element(devNs + "remarks"), mamlNs);
            if (string.IsNullOrWhiteSpace(remarks))
                remarks = JoinParagraphs(example.Element(mamlNs + "remarks"), mamlNs);
            if (!string.IsNullOrWhiteSpace(remarks))
                narrativeParts.Add(remarks);

            if (narrativeParts.Count > 0)
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "text",
                    Text = string.Join(Environment.NewLine + Environment.NewLine, narrativeParts)
                });
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "code",
                    Text = code
                });
            }
        }
    }

    private static string NormalizePowerShellExampleTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var normalized = title.Trim();
        normalized = normalized.Trim('-').Trim();
        return normalized;
    }

    private static string ResolvePowerShellCommandKind(string commandName, PowerShellCommandKindHints hints, string? commandTypeRaw)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return "Cmdlet";
        var commandType = NormalizePowerShellCommandType(commandTypeRaw);
        if (!string.IsNullOrWhiteSpace(commandType))
            return commandType;
        if (commandName.StartsWith("about_", StringComparison.OrdinalIgnoreCase))
            return "About";
        if (hints.Aliases.Contains(commandName))
            return "Alias";
        if (hints.Functions.Contains(commandName))
            return "Function";
        if (hints.Cmdlets.Contains(commandName))
            return "Cmdlet";
        if (hints.FunctionsWildcard && !hints.CmdletsWildcard)
            return "Function";
        if (hints.CmdletsWildcard && !hints.FunctionsWildcard)
            return "Cmdlet";
        if (hints.HasSignals)
            return "Command";
        return "Cmdlet";
    }

    private static string? NormalizePowerShellCommandType(string? commandTypeRaw)
    {
        if (string.IsNullOrWhiteSpace(commandTypeRaw))
            return null;

        var normalized = commandTypeRaw.Trim();
        if (normalized.Contains('.', StringComparison.Ordinal))
            normalized = normalized[(normalized.LastIndexOf('.') + 1)..];

        return normalized.ToLowerInvariant() switch
        {
            "cmdlet" => "Cmdlet",
            "function" => "Function",
            "filter" => "Function",
            "script" => "Function",
            "externalscript" => "Function",
            "alias" => "Alias",
            _ => null
        };
    }

    private static void AppendPowerShellAboutTopics(
        ApiDocModel apiDoc,
        string inputHelpPath,
        string resolvedHelpPath,
        string moduleName,
        string? manifestPath,
        List<string> warnings)
    {
        var existing = new HashSet<string>(apiDoc.Types.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var file in ResolvePowerShellAboutTopicFiles(inputHelpPath, resolvedHelpPath, manifestPath))
        {
            var aboutName = GetAboutTopicNameFromFile(file);
            if (string.IsNullOrWhiteSpace(aboutName))
                continue;
            if (existing.Contains(aboutName))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                warnings?.Add($"PowerShell about topic skipped: {Path.GetFileName(file)} ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            var normalized = NormalizePowerShellAboutText(content);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            var summary = GetPowerShellAboutSummary(normalized, aboutName);
            var remarks = GetPowerShellAboutRemarks(normalized, aboutName);

            var type = new ApiTypeModel
            {
                Name = aboutName,
                FullName = aboutName,
                Namespace = moduleName ?? string.Empty,
                Kind = "About",
                Slug = Slugify(aboutName),
                Summary = summary,
                Remarks = remarks
            };

            apiDoc.Types[type.FullName] = type;
            existing.Add(type.FullName);
        }
    }

    private static IReadOnlyList<string> ResolvePowerShellAboutTopicFiles(string inputHelpPath, string resolvedHelpPath, string? manifestPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(inputHelpPath) && Directory.Exists(inputHelpPath))
            roots.Add(Path.GetFullPath(inputHelpPath));

        var helpDir = Path.GetDirectoryName(resolvedHelpPath);
        if (!string.IsNullOrWhiteSpace(helpDir) && Directory.Exists(helpDir))
        {
            roots.Add(Path.GetFullPath(helpDir));
            var parent = Directory.GetParent(helpDir);
            if (parent is not null && parent.Exists)
                roots.Add(parent.FullName);
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifestDir) && Directory.Exists(manifestDir))
                roots.Add(Path.GetFullPath(manifestDir));
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var pattern in new[] { "about_*.help.txt", "about_*.txt", "about_*.md", "about_*.markdown" })
            {
                IEnumerable<string> matches;
                try
                {
                    matches = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (var match in matches)
                    files.Add(match);
            }
        }

        return files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? GetAboutTopicNameFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = fileName.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".help.txt".Length]
            : Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("about_", StringComparison.OrdinalIgnoreCase))
            return null;

        return name;
    }

    private static string NormalizePowerShellAboutText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;
        var normalized = content.Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        return normalized;
    }

    private static string? GetPowerShellAboutSummary(string text, string aboutName)
    {
        var lines = text.Split('\n')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (lines.Count == 0)
            return null;

        foreach (var line in lines)
        {
            var normalized = line.TrimStart('#').Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (string.Equals(normalized, aboutName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(normalized, "TOPIC", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(normalized, "SHORT DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(normalized, "LONG DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                continue;
            return normalized;
        }

        return lines[0];
    }

    private static string? GetPowerShellAboutRemarks(string text, string aboutName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n')
            .Select(x => x.TrimEnd())
            .ToList();

        if (lines.Count > 0)
        {
            var first = lines[0].TrimStart('#').Trim();
            if (string.Equals(first, aboutName, StringComparison.OrdinalIgnoreCase))
                lines.RemoveAt(0);
        }

        var remarks = string.Join(Environment.NewLine, lines).Trim();
        return string.IsNullOrWhiteSpace(remarks) ? null : remarks;
    }

    private static string? TryResolvePowerShellModuleManifestPath(string resolvedHelpPath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(resolvedHelpPath))
            return null;

        var startDir = Path.GetDirectoryName(resolvedHelpPath);
        if (string.IsNullOrWhiteSpace(startDir))
            return null;

        var directories = new List<string>();
        var current = Path.GetFullPath(startDir);
        for (var depth = 0; depth < 6 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            directories.Add(current);
            var parent = Directory.GetParent(current);
            if (parent is null)
                break;
            if (string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent.FullName;
        }

        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                continue;
            var exact = Path.Combine(dir, moduleName + ".psd1");
            if (File.Exists(exact))
                return exact;
        }

        foreach (var dir in directories)
        {
            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(dir, "*.psd1", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }
            if (manifests.Length == 1)
                return manifests[0];
        }

        return null;
    }

    private sealed class PowerShellParameterInfo
    {
        public string? Summary { get; set; }
        public string? Type { get; set; }
        public bool Required { get; set; }
        public string? DefaultValue { get; set; }
    }
}
