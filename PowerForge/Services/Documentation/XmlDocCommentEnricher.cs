using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Enriches help extracted via <c>Get-Help</c> with information from C# XML documentation
/// files (<c>*.xml</c>) for binary cmdlets.
/// </summary>
internal sealed class XmlDocCommentEnricher
{
    private readonly ILogger _logger;

    public XmlDocCommentEnricher(ILogger logger) => _logger = logger;

    public void Enrich(DocumentationExtractionPayload payload)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (payload.Commands.Count == 0) return;

        var cmdlets = payload.Commands
            .Where(c => c is not null)
            .Where(c => c.CommandType.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.ImplementingType))
            .Where(c => !string.IsNullOrWhiteSpace(c.AssemblyPath))
            .ToArray();

        if (cmdlets.Length == 0) return;

        var xmlCache = new Dictionary<string, XmlDocFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in cmdlets)
        {
            var implementingType = cmd.ImplementingType!.Trim();
            var assemblyPathRaw = cmd.AssemblyPath!.Trim().Trim('"');

            string assemblyPath;
            try { assemblyPath = Path.GetFullPath(assemblyPathRaw); }
            catch { assemblyPath = assemblyPathRaw; }

            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                continue;

            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
            {
                if (_logger.IsVerbose)
                    _logger.Verbose($"XML docs not found for '{cmd.Name}' ({assemblyPath}). Expected: {xmlPath}");
                continue;
            }

            if (!xmlCache.TryGetValue(xmlPath, out var xml))
            {
                try
                {
                    xml = XmlDocFile.Load(xmlPath);
                    xmlCache[xmlPath] = xml;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to read XML docs '{xmlPath}'. Error: {ex.Message}");
                    continue;
                }
            }

            var typeMember = xml.TryGetMember("T:" + implementingType);
            if (typeMember is not null)
            {
                if (NeedsSynopsis(cmd.Name, cmd.Synopsis) && !string.IsNullOrWhiteSpace(typeMember.Summary))
                    cmd.Synopsis = typeMember.Summary!;

                if (NeedsText(cmd.Description) || LooksLikeSynopsisOnly(cmd.Description, cmd.Synopsis))
                {
                    var desc = typeMember.Remarks;
                    if (string.IsNullOrWhiteSpace(desc)) desc = typeMember.Body;
                    if (string.IsNullOrWhiteSpace(desc)) desc = typeMember.Summary;
                    if (!string.IsNullOrWhiteSpace(desc)) cmd.Description = desc!;
                }

                if (!HasMeaningfulExamples(cmd.Examples) && typeMember.Examples.Length > 0)
                {
                    cmd.Examples ??= new List<DocumentationExampleHelp>();      
                    foreach (var ex in typeMember.Examples)
                    {
                        cmd.Examples.Add(new DocumentationExampleHelp
                        {
                            Title = ex.Title ?? string.Empty,
                            Introduction = ex.Introduction ?? string.Empty,
                            Code = ex.Code ?? string.Empty,
                            Remarks = ex.Remarks ?? string.Empty
                        });
                    }
                }

                if ((cmd.RelatedLinks?.Count ?? 0) == 0 && typeMember.SeeAlso.Length > 0)
                {
                    cmd.RelatedLinks ??= new List<DocumentationLinkHelp>();
                    foreach (var link in typeMember.SeeAlso)
                    {
                        if (string.IsNullOrWhiteSpace(link.Uri) && string.IsNullOrWhiteSpace(link.Text))
                            continue;

                        cmd.RelatedLinks.Add(new DocumentationLinkHelp
                        {
                            Text = link.Text ?? string.Empty,
                            Uri = link.Uri ?? string.Empty
                        });
                    }
                }

                if ((cmd.Notes?.Count ?? 0) == 0 && typeMember.Alerts.Length > 0)
                {
                    cmd.Notes ??= new List<DocumentationNoteHelp>();
                    foreach (var alert in typeMember.Alerts)
                    {
                        if (string.IsNullOrWhiteSpace(alert.Title) && string.IsNullOrWhiteSpace(alert.Text))
                            continue;

                        cmd.Notes.Add(new DocumentationNoteHelp
                        {
                            Title = alert.Title ?? string.Empty,
                            Text = alert.Text ?? string.Empty
                        });
                    }
                }
            }

            foreach (var p in cmd.Parameters ?? Enumerable.Empty<DocumentationParameterHelp>())
            {
                if (p is null) continue;
                if (!NeedsText(p.Description)) continue;
                if (string.IsNullOrWhiteSpace(p.Name)) continue;

                var member =
                    xml.TryGetMember("P:" + implementingType + "." + p.Name.Trim()) ??
                    xml.TryGetMember("F:" + implementingType + "." + p.Name.Trim());

                if (member is null) continue;

                var desc = member.Remarks;
                if (string.IsNullOrWhiteSpace(desc)) desc = member.Body;
                if (string.IsNullOrWhiteSpace(desc)) desc = member.Summary;
                if (!string.IsNullOrWhiteSpace(desc)) p.Description = desc!;
            }

            EnrichTypeDescriptions(cmd.Inputs, xml);
            EnrichTypeDescriptions(cmd.Outputs, xml);
        }
    }

    private static void EnrichTypeDescriptions(IEnumerable<DocumentationTypeHelp>? entries, XmlDocFile xml)
    {
        foreach (var entry in entries ?? Enumerable.Empty<DocumentationTypeHelp>())
        {
            if (entry is null || !NeedsText(entry.Description))
                continue;

            foreach (var key in GetTypeLookupKeys(entry))
            {
                var member = xml.TryGetTypeMember(key);
                if (member is null)
                    continue;

                var description = member.Remarks;
                if (string.IsNullOrWhiteSpace(description))
                    description = member.Body;
                if (string.IsNullOrWhiteSpace(description))
                    description = member.Summary;

                if (!string.IsNullOrWhiteSpace(description))
                {
                    entry.Description = description!;
                    break;
                }
            }
        }
    }

    private static IEnumerable<string> GetTypeLookupKeys(DocumentationTypeHelp entry)
    {
        var ordered = new List<string>(2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? value)
        {
            var candidate = NormalizeTypeLookupKey(value);
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            if (seen.Add(candidate))
                ordered.Add(candidate);
        }

        AddCandidate(entry.ClrTypeName);
        AddCandidate(entry.Name);

        foreach (var value in ordered)
            yield return value;
    }

    private static string NormalizeTypeLookupKey(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        while (candidate.EndsWith("[]", StringComparison.Ordinal))
            candidate = candidate.Substring(0, candidate.Length - 2);

        var genericTick = candidate.IndexOf('`');
        if (genericTick >= 0)
            candidate = candidate.Substring(0, genericTick);

        var genericBracket = candidate.IndexOf('[');
        if (genericBracket >= 0)
            candidate = candidate.Substring(0, genericBracket);

        return candidate.Trim();
    }

    private static bool HasMeaningfulExamples(IReadOnlyList<DocumentationExampleHelp>? examples)
    {
        if (examples is null || examples.Count == 0) return false;

        foreach (var ex in examples)
        {
            if (ex is null) continue;
            if (!NeedsText(ex.Introduction)) return true;
            if (!NeedsText(ex.Code)) return true;
            if (!NeedsText(ex.Remarks)) return true;
        }

        return false;
    }

    private static bool NeedsText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value!.Trim();
        if (v.StartsWith("{{", StringComparison.Ordinal)) return true;
        if (v.Contains(@"C:\Path", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Contains("C:/Path", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Contains("Fill in", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Contains("does not have", StringComparison.OrdinalIgnoreCase) && v.Contains("help", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool LooksLikeSynopsisOnly(string? description, string? synopsis)
    {
        if (string.IsNullOrWhiteSpace(description)) return true;
        if (string.IsNullOrWhiteSpace(synopsis)) return false;
        return string.Equals(description!.Trim(), synopsis!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsSynopsis(string commandName, string? synopsis)
    {
        if (NeedsText(synopsis)) return true;

        var name = (commandName ?? string.Empty).Trim();
        var text = synopsis!.Trim();

        // When no help exists, Get-Help often returns the syntax line as the synopsis.
        if (!string.IsNullOrWhiteSpace(name) &&
            text.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("[<CommonParameters>]", StringComparison.OrdinalIgnoreCase) ||
             text.Contains(" -", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private sealed class XmlDocFile
    {
        private static readonly Regex CrefTokenRegex = new(
            @"\b([A-Z]):([A-Za-z_][A-Za-z0-9_`]*(?:\.[A-Za-z_][A-Za-z0-9_`]*)*(?:\[\])?)\b",
            RegexOptions.CultureInvariant);

        private readonly Dictionary<string, XmlDocMember> _members;

        private XmlDocFile(Dictionary<string, XmlDocMember> members) => _members = members;

        public static XmlDocFile Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(full)) throw new FileNotFoundException("XML docs file not found.", full);

            using var stream = File.OpenRead(full);
            var doc = XDocument.Load(stream, LoadOptions.None);
            var members = new Dictionary<string, XmlDocMember>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in doc.Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name)) continue;

                members[name!] = new XmlDocMember(
                    summary: ExtractParagraphText(member.Element("summary")),
                    remarks: ExtractParagraphText(member.Element("remarks")),
                    body: ExtractBodyText(member),
                    examples: ExtractExamples(member).ToArray(),
                    seeAlso: ExtractSeeAlso(member).ToArray(),
                    alerts: ExtractAlerts(member).ToArray());
            }

            return new XmlDocFile(members);
        }

        public XmlDocMember? TryGetMember(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _members.TryGetValue(name, out var m) ? m : null;
        }

        public XmlDocMember? TryGetTypeMember(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            if (_members.TryGetValue("T:" + typeName.Trim(), out var exact))
                return exact;

            var simpleName = typeName.Trim();
            var lastDot = simpleName.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < simpleName.Length - 1)
                simpleName = simpleName.Substring(lastDot + 1);

            XmlDocMember? singleMatch = null;
            foreach (var pair in _members.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!pair.Key.StartsWith("T:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var current = pair.Key.Substring(2);
                if (current.Equals(typeName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return pair.Value;

                var currentLastDot = current.LastIndexOf('.');
                var currentSimple = currentLastDot >= 0 && currentLastDot < current.Length - 1
                    ? current.Substring(currentLastDot + 1)
                    : current;

                if (currentSimple.Equals(simpleName, StringComparison.OrdinalIgnoreCase))
                {
                    // PowerShell help sometimes only reports short type names. Use that as a best-effort fallback
                    // only when it uniquely identifies one XML-doc type member; otherwise prefer no enrichment
                    // over an arbitrary first-match-wins description.
                    if (singleMatch is not null)
                        return null;

                    singleMatch = pair.Value;
                }
            }

            return singleMatch;
        }

        private static IEnumerable<XmlDocExample> ExtractExamples(XElement member)
        {
            var examples = member.Elements("example").ToArray();
            if (examples.Length == 0) yield break;

            for (var i = 0; i < examples.Length; i++)
            {
                var ex = examples[i];
                var title = ExtractParagraphText(ex.Element("summary"));
                if (string.IsNullOrWhiteSpace(title)) title = $"EXAMPLE {i + 1}";

                var prefix = ExtractPreservedInlineText(ex.Element("prefix"));
                var code = ExtractCodeText(ex.Element("code"));

                var remarks = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    ex.Elements("para").Select(ExtractParagraphText).Where(s => !string.IsNullOrWhiteSpace(s)));

                yield return new XmlDocExample(title, prefix, code, remarks);
            }
        }

        private static string ExtractBodyText(XElement member)
        {
            var paras = member.Elements()
                .Where(element => element.Name.LocalName.Equals("para", StringComparison.OrdinalIgnoreCase))
                .Select(ExtractParagraphText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            if (paras.Length > 0)
                return string.Join(Environment.NewLine + Environment.NewLine, paras);

            var textNodes = member.Nodes()
                .OfType<XText>()
                .Select(node => NormalizeText(node.Value))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            return textNodes.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine + Environment.NewLine, textNodes);
        }

        private static IEnumerable<XmlDocLink> ExtractSeeAlso(XElement member)
        {
            foreach (var see in member.Descendants("seealso"))
            {
                var href = see.Attribute("href")?.Value?.Trim();
                var cref = see.Attribute("cref")?.Value?.Trim();
                var text = ExtractParagraphText(see);

                var uri = !string.IsNullOrWhiteSpace(href) ? href : string.Empty;
                if (string.IsNullOrWhiteSpace(uri) && !string.IsNullOrWhiteSpace(cref) && cref!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    uri = cref;

                if (string.IsNullOrWhiteSpace(text))
                    text = !string.IsNullOrWhiteSpace(cref) ? cref! : string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                    text = NormalizeCrefLikeTokens(text);

                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(uri))
                    continue;

                yield return new XmlDocLink(text, uri);
            }
        }

        private static IEnumerable<XmlDocAlert> ExtractAlerts(XElement member)
        {
            foreach (var list in member.Elements()
                         .Where(element => element.Name.LocalName.Equals("list", StringComparison.OrdinalIgnoreCase) &&
                                           string.Equals(element.Attribute("type")?.Value, "alertSet", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var item in list.Elements().Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)))
                {
                    var title = ExtractParagraphText(item.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("term", StringComparison.OrdinalIgnoreCase)));
                    var description = item.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase));
                    var text = description is null ? string.Empty : ExtractParagraphText(description);
                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
                        continue;

                    yield return new XmlDocAlert(title, text);
                }
            }
        }

        private static string ExtractCodeText(XElement? element)
        {
            if (element is null)
                return string.Empty;

            var value = element.Value.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = value.Split('\n')
                .SkipWhile(string.IsNullOrWhiteSpace)
                .Reverse()
                .SkipWhile(string.IsNullOrWhiteSpace)
                .Reverse()
                .ToArray();

            var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (nonEmptyLines.Length > 0)
            {
                var indent = nonEmptyLines.Min(line => line.TakeWhile(ch => ch == ' ' || ch == '\t').Count());
                if (indent > 0)
                {
                    lines = lines.Select(line =>
                    {
                        var removable = Math.Min(indent, line.TakeWhile(ch => ch == ' ' || ch == '\t').Count());
                        return removable == 0 ? line : line.Substring(removable);
                    }).ToArray();
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string ExtractPreservedInlineText(XElement? element)
        {
            if (element is null)
                return string.Empty;

            return ExtractInlineText(element)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim('\r', '\n');
        }

        private static string ExtractParagraphText(XElement? element)
        {
            if (element is null) return string.Empty;

            var paras = element.Elements("para").ToArray();
            if (paras.Length > 0)
            {
                var parts = paras.Select(ExtractInlineText).Select(NormalizeText).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                return string.Join(Environment.NewLine + Environment.NewLine, parts);
            }

            return NormalizeText(ExtractInlineText(element));
        }

        private static string ExtractInlineText(XElement element)
        {
            var sb = new StringBuilder();
            foreach (var node in element.Nodes())
            {
                switch (node)
                {
                    case XText t:
                        sb.Append(t.Value);
                        break;
                    case XElement e:
                        sb.Append(ExtractElementText(e));
                        break;
                }
            }
            return sb.ToString();
        }

        private static string ExtractElementText(XElement element)
        {
            var name = element.Name.LocalName;

            if (name.Equals("para", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractInlineText(element);
                return Environment.NewLine + Environment.NewLine + inner;
            }

            if (name.Equals("code", StringComparison.OrdinalIgnoreCase))
                return element.Value;

            if (name.Equals("paramref", StringComparison.OrdinalIgnoreCase))
                return element.Attribute("name")?.Value ?? string.Empty;

            if (name.Equals("see", StringComparison.OrdinalIgnoreCase) || name.Equals("seealso", StringComparison.OrdinalIgnoreCase))
            {
                var href = element.Attribute("href")?.Value;
                var cref = element.Attribute("cref")?.Value;
                var text = element.Value;
                if (!string.IsNullOrWhiteSpace(text)) return text;
                if (!string.IsNullOrWhiteSpace(href)) return href!;
                if (!string.IsNullOrWhiteSpace(cref)) return SimplifyCrefToken(cref!);
                return string.Empty;
            }

            return ExtractInlineText(element);
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var v = value.Replace("\r\n", "\n");
            var lines = v.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            return NormalizeCrefLikeTokens(string.Join(Environment.NewLine, lines));
        }

        private static string NormalizeCrefLikeTokens(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            return CrefTokenRegex.Replace(input, match =>
            {
                var token = match.Value;
                var simplified = SimplifyCrefToken(token);
                return string.IsNullOrWhiteSpace(simplified) ? token : simplified;
            });
        }

        private static string SimplifyCrefToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;

            var trimmed = token.Trim();
            if (trimmed.Length < 4 || trimmed[1] != ':')
                return trimmed;

            var body = trimmed.Substring(2);
            var methodSigIndex = body.IndexOf('(');
            if (methodSigIndex >= 0)
                body = body.Substring(0, methodSigIndex);

            var lastDot = body.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < body.Length - 1)
                body = body.Substring(lastDot + 1);

            return string.IsNullOrWhiteSpace(body) ? trimmed : body;
        }
    }

    private sealed class XmlDocExample
    {
        public string? Title { get; }
        public string? Introduction { get; }
        public string? Code { get; }
        public string? Remarks { get; }

        public XmlDocExample(string? title, string? introduction, string? code, string? remarks)
        {
            Title = title;
            Introduction = introduction;
            Code = code;
            Remarks = remarks;
        }
    }

    private sealed class XmlDocLink
    {
        public string? Text { get; }
        public string? Uri { get; }

        public XmlDocLink(string? text, string? uri)
        {
            Text = text;
            Uri = uri;
        }
    }

    private sealed class XmlDocAlert
    {
        public string? Title { get; }
        public string? Text { get; }

        public XmlDocAlert(string? title, string? text)
        {
            Title = title;
            Text = text;
        }
    }

    private sealed class XmlDocMember
    {
        public string? Summary { get; }
        public string? Remarks { get; }
        public string? Body { get; }
        public XmlDocExample[] Examples { get; }
        public XmlDocLink[] SeeAlso { get; }
        public XmlDocAlert[] Alerts { get; }

        public XmlDocMember(string? summary, string? remarks, string? body, XmlDocExample[] examples, XmlDocLink[] seeAlso, XmlDocAlert[] alerts)
        {
            Summary = summary;
            Remarks = remarks;
            Body = body;
            Examples = examples ?? Array.Empty<XmlDocExample>();
            SeeAlso = seeAlso ?? Array.Empty<XmlDocLink>();
            Alerts = alerts ?? Array.Empty<XmlDocAlert>();
        }
    }
}
