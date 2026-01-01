using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                if (string.IsNullOrWhiteSpace(desc)) desc = member.Summary;
                if (!string.IsNullOrWhiteSpace(desc)) p.Description = desc!;
            }
        }
    }

    private static bool HasMeaningfulExamples(IReadOnlyList<DocumentationExampleHelp>? examples)
    {
        if (examples is null || examples.Count == 0) return false;

        foreach (var ex in examples)
        {
            if (ex is null) continue;
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
                    examples: ExtractExamples(member).ToArray(),
                    seeAlso: ExtractSeeAlso(member).ToArray());
            }

            return new XmlDocFile(members);
        }

        public XmlDocMember? TryGetMember(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _members.TryGetValue(name, out var m) ? m : null;
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

                var prefix = ExtractParagraphText(ex.Element("prefix"));
                var code = ExtractParagraphText(ex.Element("code"));
                var fullCode = CombinePrefixAndCode(prefix, code);

                var remarks = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    ex.Elements("para").Select(ExtractParagraphText).Where(s => !string.IsNullOrWhiteSpace(s)));

                yield return new XmlDocExample(title, fullCode, remarks);
            }
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

                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(uri))
                    continue;

                yield return new XmlDocLink(text, uri);
            }
        }

        private static string CombinePrefixAndCode(string? prefix, string? code)
        {
            var p = (prefix ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');
            var c = (code ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');

            if (string.IsNullOrWhiteSpace(p)) return c;
            if (string.IsNullOrWhiteSpace(c)) return p;

            // Avoid duplicate "PS> PS>" if docs already embed it in code.
            if (c.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return c;

            return p + c;
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
                if (!string.IsNullOrWhiteSpace(cref)) return cref!;
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
            return string.Join(Environment.NewLine, lines);
        }
    }

    private sealed class XmlDocExample
    {
        public string? Title { get; }
        public string? Code { get; }
        public string? Remarks { get; }

        public XmlDocExample(string? title, string? code, string? remarks)
        {
            Title = title;
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

    private sealed class XmlDocMember
    {
        public string? Summary { get; }
        public string? Remarks { get; }
        public XmlDocExample[] Examples { get; }
        public XmlDocLink[] SeeAlso { get; }

        public XmlDocMember(string? summary, string? remarks, XmlDocExample[] examples, XmlDocLink[] seeAlso)
        {
            Summary = summary;
            Remarks = remarks;
            Examples = examples ?? Array.Empty<XmlDocExample>();
            SeeAlso = seeAlso ?? Array.Empty<XmlDocLink>();
        }
    }
}
