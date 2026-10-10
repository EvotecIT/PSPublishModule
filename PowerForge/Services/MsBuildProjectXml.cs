using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>Identifies evaluated MSBuild XML roles and their unchanged source intervals.</summary>
internal static class MsBuildProjectXml
{
    internal static bool IsEvaluationProperty(XElement element) => element.Parent?.Name.LocalName == "PropertyGroup" &&
        IsEvaluationContainer(element.Parent.Parent);

    private static bool IsEvaluationContainer(XElement? element)
    {
        for (; element is not null; element = element.Parent)
        {
            if (element == element.Document?.Root)
                return element.Name.LocalName == "Project";
            if (element.Name.LocalName != "Choose" && element.Name.LocalName != "When" && element.Name.LocalName != "Otherwise")
                return false;
        }
        return false;
    }

    internal static ElementSpan[] FindProperties(string content, string name) => FindElements(content,
        element => IsEvaluationProperty(element) && element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static ElementSpan[] FindPropertyGroups(string content) => FindElements(content,
        element => element.Name.LocalName == "PropertyGroup" && IsEvaluationContainer(element.Parent));

    internal static ElementSpan FindRoot(string content) => FindElements(content, element => element == element.Document?.Root).Single();

    private static ElementSpan[] FindElements(string content, Func<XElement, bool> predicate)
    {
        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\r')
            {
                if (index + 1 < content.Length && content[index + 1] == '\n')
                    index++;
                lineStarts.Add(index + 1);
            }
            else if (content[index] == '\n')
                lineStarts.Add(index + 1);
        }
        int Offset(IXmlLineInfo position, int prefixLength) => lineStarts[position.LineNumber - 1] + position.LinePosition - prefixLength;
        var document = XDocument.Parse(content, LoadOptions.SetLineInfo);
        var starts = document.Descendants().Where(predicate).ToDictionary(element => Offset((IXmlLineInfo)element, 2));
        var pending = new Dictionary<int, ElementSpan>();
        var spans = new List<ElementSpan>();
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var position = (IXmlLineInfo)reader;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var start = Offset(position, 2);
                if (!starts.TryGetValue(start, out var element))
                    continue;
                var openingEnd = MarkupEnd(content, start);
                var span = new ElementSpan(element, reader.Name, start, openingEnd, reader.IsEmptyElement ? openingEnd : -1, openingEnd);
                if (reader.IsEmptyElement)
                    spans.Add(span);
                else
                    pending.Add(reader.Depth, span);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && pending.TryGetValue(reader.Depth, out var start))
            {
                var closing = Offset(position, 3);
                spans.Add(new ElementSpan(start.Element, start.QualifiedName, start.Index, start.OpeningEnd, closing, MarkupEnd(content, closing)));
                pending.Remove(reader.Depth);
            }
        }
        return spans.OrderBy(span => span.Index).ToArray();
    }

    private static int MarkupEnd(string content, int start)
    {
        var quote = '\0';
        for (var index = start; index < content.Length; index++)
        {
            var value = content[index];
            if (quote != '\0')
            {
                if (value == quote)
                    quote = '\0';
            }
            else if (value == '\'' || value == '"')
                quote = value;
            else if (value == '>')
                return index + 1;
        }
        throw new InvalidOperationException("Unable to locate an MSBuild element source interval.");
    }

    internal readonly struct ElementSpan
    {
        internal ElementSpan(XElement element, string qualifiedName, int index, int openingEnd, int closingStart, int end)
        { Element = element; QualifiedName = qualifiedName; Index = index; OpeningEnd = openingEnd; ClosingStart = closingStart; Length = end - index; }
        internal XElement Element { get; }
        internal string QualifiedName { get; }
        internal int Index { get; }
        internal int Length { get; }
        internal int OpeningEnd { get; }
        internal int ClosingStart { get; }
        internal bool IsEmpty => Index + Length == OpeningEnd;
    }
}
