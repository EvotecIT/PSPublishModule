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

    private static ApiDocModel ParsePowerShellHelp(string helpPath, List<string> warnings, WebApiDocsOptions options)
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
            var (returns, outputTypes) = ParsePowerShellReturnInfo(command, commandNs, mamlNs, devNs);
            var inputTypes = ParsePowerShellInputTypes(command, commandNs, mamlNs, devNs);
            if (outputTypes.Count == 0)
            {
                foreach (var outputType in ParsePowerShellTypeNames(command.Element(commandNs + "outputTypes"), commandNs, mamlNs, devNs, "outputType"))
                {
                    if (!outputTypes.Contains(outputType, StringComparer.OrdinalIgnoreCase))
                        outputTypes.Add(outputType);
                }
            }
            var commandAliases = ParsePowerShellCommandAliases(command, details, commandNs, mamlNs);

            var commandKind = ResolvePowerShellCommandKind(name!, kindHints, details?.Element(commandNs + "commandType")?.Value);
            var type = new ApiTypeModel
            {
                Name = name!,
                FullName = name!,
                Namespace = moduleName ?? string.Empty,
                Kind = commandKind,
                Slug = Slugify(name!),
                Summary = summary,
                Remarks = remarks
            };
            foreach (var alias in commandAliases)
                type.Aliases.Add(alias);
            foreach (var inputType in inputTypes)
                type.InputTypes.Add(inputType);
            foreach (var outputType in outputTypes)
                type.OutputTypes.Add(outputType);

            var syntax = command.Element(commandNs + "syntax");
            var commandParameterMap = BuildPowerShellParameterMap(command, commandNs, mamlNs, devNs);
            var includesCommonParameters = SupportsPowerShellCommonParameters(command, details, commandNs, mamlNs, commandKind);
            if (syntax is not null)
            {
                foreach (var syntaxItem in syntax.Elements(commandNs + "syntaxItem"))
                {
                    var member = new ApiMemberModel
                    {
                        Name = name!,
                        Kind = "CommandSyntax",
                        ParameterSetName = ResolvePowerShellParameterSetName(syntaxItem, commandNs, mamlNs, devNs),
                        IncludesCommonParameters = includesCommonParameters,
                        Returns = returns,
                        ReturnType = type.OutputTypes.Count == 1 ? type.OutputTypes[0] : null
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
                        var position = parameter.Attribute("position")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(position))
                            position = commandParameter?.Position;
                        var pipelineInput = parameter.Attribute("pipelineInput")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(pipelineInput))
                            pipelineInput = commandParameter?.PipelineInput;
                        var aliases = ParsePowerShellAliases(parameter.Attribute("aliases")?.Value);
                        if (aliases.Count == 0 && commandParameter is not null)
                            aliases.AddRange(commandParameter.Aliases);

                        var parameterModel = new ApiParameterModel
                        {
                            Name = paramName,
                            Type = paramType,
                            Summary = paramSummary,
                            IsOptional = !isRequired,
                            DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue,
                            Position = string.IsNullOrWhiteSpace(position) ? null : position,
                            PipelineInput = string.IsNullOrWhiteSpace(pipelineInput) ? null : pipelineInput
                        };
                        foreach (var alias in aliases.Distinct(StringComparer.OrdinalIgnoreCase))
                            parameterModel.Aliases.Add(alias);
                        member.Parameters.Add(parameterModel);
                    }
                    member.Signature = BuildPowerShellSyntaxSignature(name!, member.Parameters, member.IncludesCommonParameters);
                    type.Methods.Add(member);
                }
                AssignPowerShellParameterSetNames(type.Methods);
            }

            AppendPowerShellExamples(type, command, commandNs, mamlNs, devNs);

            apiDoc.Types[type.FullName] = type;
        }

        AppendPowerShellFallbackExamples(apiDoc, helpPath, resolved, manifestPath, options, warnings);
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
        var paragraphs = ExtractPowerShellParagraphs(parent, mamlNs);
        return paragraphs.Count == 0 ? null : paragraphs[0];
    }

    private static string? JoinParagraphs(XElement? parent, XNamespace mamlNs)
    {
        var paragraphs = ExtractPowerShellParagraphs(parent, mamlNs);
        return paragraphs.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    private static List<string> ExtractPowerShellParagraphs(XElement? parent, XNamespace mamlNs)
    {
        var paragraphs = new List<string>();
        if (parent is null)
            return paragraphs;

        void AppendNormalized(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var normalized = value.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                paragraphs.Add(normalized);
        }

        foreach (var para in parent.Elements(mamlNs + "para"))
            AppendNormalized(para.Value);

        if (paragraphs.Count == 0)
        {
            foreach (var para in parent.Descendants(mamlNs + "para"))
                AppendNormalized(para.Value);
        }

        if (paragraphs.Count == 0)
        {
            foreach (var block in SplitPowerShellParagraphText(parent.Value))
                AppendNormalized(block);
        }

        return paragraphs;
    }

    private static IEnumerable<string> SplitPowerShellParagraphText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var blocks = ParagraphSplitRegex.Split(normalized)
            .Select(block => block.Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => ParagraphLineBreakNormalizeRegex.Replace(block, " "))
            .ToList();

        return blocks;
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
            var position = parameter.Attribute("position")?.Value?.Trim();
            var pipelineInput = parameter.Attribute("pipelineInput")?.Value?.Trim();
            var aliases = ParsePowerShellAliases(parameter.Attribute("aliases")?.Value);

            map[name] = new PowerShellParameterInfo
            {
                Summary = summary,
                Type = type,
                Required = required,
                DefaultValue = defaultValue,
                Position = position,
                PipelineInput = pipelineInput,
                Aliases = aliases
            };
        }

        return map;
    }

    private static (string? text, List<string> outputTypes) ParsePowerShellReturnInfo(
        XElement command,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs)
    {
        var outputTypes = new List<string>();
        var values = command.Element(commandNs + "returnValues");
        if (values is null)
            return (null, outputTypes);

        var parts = new List<string>();
        foreach (var value in values.Elements(commandNs + "returnValue"))
        {
            var description = JoinParagraphs(value.Element(mamlNs + "description"), mamlNs);
            if (string.IsNullOrWhiteSpace(description))
                description = JoinParagraphs(value.Element(devNs + "remarks"), mamlNs);

            var typeNames = ParsePowerShellTypeNames(value, commandNs, mamlNs, devNs);
            foreach (var typeName in typeNames)
            {
                if (!outputTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                    outputTypes.Add(typeName);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                if (typeNames.Count > 0)
                    parts.Add($"{string.Join(", ", typeNames)}: {description}");
                else
                    parts.Add(description);
            }
        }

        if (parts.Count == 0 && outputTypes.Count > 0)
            parts.Add(string.Join(", ", outputTypes));

        return (parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts), outputTypes);
    }

    private static List<string> ParsePowerShellInputTypes(
        XElement command,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs)
    {
        var inputs = command.Element(commandNs + "inputTypes");
        return ParsePowerShellTypeNames(inputs, commandNs, mamlNs, devNs, "inputType");
    }

    private static List<string> ParsePowerShellCommandAliases(
        XElement command,
        XElement? details,
        XNamespace commandNs,
        XNamespace mamlNs)
    {
        var aliases = new List<string>();

        void AppendFrom(XElement? parent)
        {
            if (parent is null)
                return;

            foreach (var alias in parent.Elements(commandNs + "alias").Select(e => e.Value))
            {
                foreach (var parsed in ParsePowerShellAliases(alias))
                    aliases.Add(parsed);
            }

            foreach (var node in parent.Elements(commandNs + "aliases"))
            {
                foreach (var alias in node.Elements(commandNs + "alias").Select(e => e.Value))
                {
                    foreach (var parsed in ParsePowerShellAliases(alias))
                        aliases.Add(parsed);
                }

                foreach (var alias in ParsePowerShellAliases(node.Value))
                    aliases.Add(alias);
            }

            foreach (var node in parent.Elements(mamlNs + "aliases"))
            {
                foreach (var alias in node.Elements(mamlNs + "alias").Select(e => e.Value))
                {
                    foreach (var parsed in ParsePowerShellAliases(alias))
                        aliases.Add(parsed);
                }

                foreach (var alias in ParsePowerShellAliases(node.Value))
                    aliases.Add(alias);
            }
        }

        AppendFrom(details);
        AppendFrom(command);

        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParsePowerShellTypeNames(
        XElement? root,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs,
        string? entryLocalName = null)
    {
        var types = new List<string>();
        if (root is null)
            return types;

        IEnumerable<XElement> entries;
        if (string.IsNullOrWhiteSpace(entryLocalName))
        {
            var selfAndDescendants = new List<XElement>();
            if (root.Name == commandNs + "returnValue" || root.Name == commandNs + "outputType" || root.Name == commandNs + "inputType")
                selfAndDescendants.Add(root);
            selfAndDescendants.AddRange(root.Descendants()
                .Where(el => el.Name == commandNs + "returnValue" || el.Name == commandNs + "outputType" || el.Name == commandNs + "inputType"));
            entries = selfAndDescendants;
        }
        else
        {
            entries = root.Elements(commandNs + entryLocalName);
        }

        foreach (var entry in entries)
        {
            var directTypeName = entry.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(directTypeName))
                types.Add(directTypeName);

            foreach (var nested in entry.Descendants(devNs + "type"))
            {
                var name = nested.Element(mamlNs + "name")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    types.Add(name);
            }

            var outputTypeName = entry.Element(commandNs + "outputTypeName")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(outputTypeName))
                types.Add(outputTypeName);

            var fallback = entry.Element(mamlNs + "name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(fallback))
                types.Add(fallback);
        }

        return types
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParsePowerShellAliases(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(alias =>
                !string.IsNullOrWhiteSpace(alias) &&
                !alias.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                !alias.Equals("(none)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SupportsPowerShellCommonParameters(
        XElement command,
        XElement? details,
        XNamespace commandNs,
        XNamespace mamlNs,
        string? commandKind)
    {
        if (string.Equals(commandKind, "Alias", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandKind, "About", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(commandKind, "Cmdlet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandKind, "Function", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandKind, "Command", StringComparison.OrdinalIgnoreCase))
            return true;

        var detailsText = details?.Value ?? string.Empty;
        var descriptionText = command.Element(mamlNs + "description")?.Value ?? string.Empty;
        var linksText = command.Element(commandNs + "relatedLinks")?.Value ?? string.Empty;
        var combined = string.Join("\n", new[] { detailsText, descriptionText, linksText });
        return combined.Contains("about_CommonParameters", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("common parameter", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePowerShellParameterSetName(
        XElement syntaxItem,
        XNamespace commandNs,
        XNamespace mamlNs,
        XNamespace devNs)
    {
        if (syntaxItem is null)
            return null;

        var candidates = new[]
        {
            syntaxItem.Attribute("parameterSetName")?.Value,
            syntaxItem.Attribute("setName")?.Value,
            syntaxItem.Element(commandNs + "parameterSetName")?.Value,
            syntaxItem.Element(mamlNs + "parameterSetName")?.Value,
            syntaxItem.Element(devNs + "parameterSetName")?.Value
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var normalized = candidate.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (normalized.Equals("__AllParameterSets", StringComparison.OrdinalIgnoreCase))
                return "All Parameter Sets";
            if (normalized.Equals("(All)", StringComparison.OrdinalIgnoreCase))
                return "All Parameter Sets";
            return normalized;
        }

        return null;
    }

    private static void AssignPowerShellParameterSetNames(List<ApiMemberModel> members)
    {
        if (members is null || members.Count <= 1)
            return;

        var parameterSets = members
            .Select(member => new HashSet<string>(
                member.Parameters
                    .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                    .Select(static parameter => parameter.Name.Trim()),
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        var requiredParameterSets = members
            .Select(member => new HashSet<string>(
                member.Parameters
                    .Where(static parameter => !parameter.IsOptional && !string.IsNullOrWhiteSpace(parameter.Name))
                    .Select(static parameter => parameter.Name.Trim()),
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        for (var index = 0; index < members.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(members[index].ParameterSetName))
                continue;

            var uniqueRequired = GetPowerShellUniqueParameterNames(requiredParameterSets, index);
            if (uniqueRequired.Count > 0)
            {
                members[index].ParameterSetName = "By " + string.Join(" + ", uniqueRequired);
                continue;
            }

            var uniqueAny = GetPowerShellUniqueParameterNames(parameterSets, index);
            if (uniqueAny.Count > 0)
            {
                members[index].ParameterSetName = "By " + string.Join(" + ", uniqueAny);
                continue;
            }

            members[index].ParameterSetName = $"Set {index + 1}";
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.ParameterSetName))
                continue;

            if (!seen.TryAdd(member.ParameterSetName, 1))
            {
                seen[member.ParameterSetName]++;
                member.ParameterSetName = $"{member.ParameterSetName} ({seen[member.ParameterSetName]})";
            }
        }
    }

    private static List<string> GetPowerShellUniqueParameterNames(List<HashSet<string>> sets, int currentIndex)
    {
        if (sets is null || currentIndex < 0 || currentIndex >= sets.Count)
            return new List<string>();

        var current = sets[currentIndex];
        if (current.Count == 0)
            return new List<string>();

        var unique = current
            .Where(parameterName => sets
                .Where((_, index) => index != currentIndex)
                .All(other => !other.Contains(parameterName)))
            .OrderBy(parameterName => parameterName, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return unique;
    }

    private static string BuildPowerShellSyntaxSignature(
        string commandName,
        IReadOnlyList<ApiParameterModel> parameters,
        bool includeCommonParameters = false)
    {
        var parts = new List<string> { commandName };
        foreach (var parameter in parameters)
        {
            if (parameter is null)
                continue;
            var name = parameter.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var token = "-" + name;
            if (!IsPowerShellSwitchParameter(parameter.Type))
            {
                var displayType = string.IsNullOrWhiteSpace(parameter.Type) ? "Object" : parameter.Type!.Trim();
                token += $" <{displayType}>";
            }

            if (parameter.IsOptional)
                token = "[" + token + "]";

            parts.Add(token);
        }

        if (includeCommonParameters)
            parts.Add("[<CommonParameters>]");

        return string.Join(" ", parts);
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

        var exampleNumber = 1;
        foreach (var example in examples.Elements(commandNs + "example"))
        {
            var title = NormalizePowerShellExampleTitle(example.Element(mamlNs + "title")?.Value);
            if (string.IsNullOrWhiteSpace(title))
                title = $"Example {exampleNumber}";
            if (!string.IsNullOrWhiteSpace(title))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "heading",
                    Text = title
                });
            }

            foreach (var introduction in ExtractPowerShellParagraphs(example.Element(mamlNs + "introduction"), mamlNs))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "text",
                    Text = introduction
                });
            }

            var code = example.Element(devNs + "code")?.Value;
            if (string.IsNullOrWhiteSpace(code))
                code = example.Element(commandNs + "code")?.Value;
            code = code?.Trim();

            if (!string.IsNullOrWhiteSpace(code))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "code",
                    Text = code
                });
            }

            foreach (var remark in ExtractPowerShellParagraphs(example.Element(devNs + "remarks"), mamlNs))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "text",
                    Text = remark
                });
            }

            if (example.Element(devNs + "remarks") is null)
            {
                foreach (var remark in ExtractPowerShellParagraphs(example.Element(mamlNs + "remarks"), mamlNs))
                {
                    type.Examples.Add(new ApiExampleModel
                    {
                        Kind = "text",
                        Text = remark
                    });
                }
            }

            exampleNumber++;
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
                foreach (var match in SafeEnumerateFiles(root, pattern, SearchOption.AllDirectories))
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
        public List<string> Aliases { get; set; } = new();
        public bool Required { get; set; }
        public string? DefaultValue { get; set; }
        public string? Position { get; set; }
        public string? PipelineInput { get; set; }
    }
}
