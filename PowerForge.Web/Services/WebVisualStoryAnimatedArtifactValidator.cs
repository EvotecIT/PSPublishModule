using System.Text;
using System.Xml;

namespace PowerForge.Web;

/// <summary>
/// Validates browser-renderable visual-story animation payloads without executing producer code.
/// </summary>
internal static partial class WebVisualStoryAnimatedArtifactValidator
{
    private readonly record struct SvgElementIdentity(
        string LocalName,
        string? Id,
        string[] Classes,
        string? InlineStyle,
        int ParentIndex,
        int Depth);

    private const int MaximumGifFrames = 240;
    private const ulong MaximumGifDecodedPixels = 128_000_000UL;
    private const int MaximumApngFrames = 240;
    private const long MaximumApngDecodedBytes = 512_000_000L;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Validates that an animated artifact matches its declared format and contains decodable content.
    /// </summary>
    /// <param name="path">Resolved local artifact path.</param>
    /// <param name="displayPath">Manifest path used in validation errors.</param>
    /// <param name="format">Normalized animated format.</param>
    internal static void Validate(string path, string displayPath, string format)
    {
        if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSvg(path, displayPath, requireAnimation: true);
            return;
        }
        if (string.Equals(format, "apng", StringComparison.OrdinalIgnoreCase))
        {
            ValidateApng(path, displayPath);
            return;
        }

        ValidateGif(path, displayPath, requireMultipleFrames: true);
    }

    internal static void ValidateSvg(string path, string displayPath, bool requireAnimation)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 64L * 1024 * 1024
            };
            using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read() && reader.NodeType != XmlNodeType.Element)
            {
                if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                {
                    throw new InvalidOperationException(
                        $"Visual-story SVG artifacts cannot contain processing instructions: {displayPath}");
                }
            }
            if (!string.Equals(reader.LocalName, "svg", StringComparison.Ordinal) ||
                !string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Visual-story animated artifact is not a valid SVG: {displayPath}");
            }
            var sawDeclarativeAnimation = false;
            var cssKeyframeNames = new HashSet<string>(StringComparer.Ordinal);
            var cssAnimationNames = new HashSet<string>(StringComparer.Ordinal);
            var cssStyleBlocks = new List<string>();
            var svgElements = new List<SvgElementIdentity> { ReadSvgElementIdentity(reader, parentIndex: -1) };
            var smilFragmentReferences = new List<SvgFragmentReference>();
            ValidatePassiveSvgContent(reader, displayPath);
            ValidateSelfContainedReferences(reader, displayPath);
            ValidateInlineStyle(reader.GetAttribute("style"), displayPath);
            var insideStyle = false;
            StringBuilder? currentStyle = null;
            var pendingAnimateMotionDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && insideStyle)
                {
                    currentStyle!.Append(reader.Value);
                    continue;
                }
                if (reader.NodeType == XmlNodeType.EndElement &&
                    string.Equals(reader.LocalName, "style", StringComparison.Ordinal) &&
                    string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                {
                    var css = currentStyle?.ToString() ?? string.Empty;
                    if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(css))
                    {
                        throw new InvalidOperationException(
                            $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
                    }
                    cssStyleBlocks.Add(css);
                    currentStyle = null;
                    insideStyle = false;
                    continue;
                }
                if (reader.NodeType == XmlNodeType.EndElement &&
                    pendingAnimateMotionDepth == reader.Depth &&
                    string.Equals(reader.LocalName, "animateMotion", StringComparison.Ordinal) &&
                    string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                {
                    pendingAnimateMotionDepth = -1;
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                ValidatePassiveSvgContent(reader, displayPath);
                ValidateSelfContainedReferences(reader, displayPath);
                if (!string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                    continue;
                var parentIndex = FindSvgParentIndex(svgElements, reader.Depth);
                svgElements.Add(ReadSvgElementIdentity(reader, parentIndex));
                if (IsDeclarativeAnimationElement(reader))
                {
                    sawDeclarativeAnimation = true;
                    AddSmilTargetReference(reader, smilFragmentReferences, mustBePath: false);
                }
                else if (string.Equals(reader.LocalName, "animateMotion", StringComparison.Ordinal) &&
                         HasEffectiveSmilTiming(reader) &&
                         !reader.IsEmptyElement)
                {
                    pendingAnimateMotionDepth = reader.Depth;
                    AddSmilTargetReference(reader, smilFragmentReferences, mustBePath: false);
                }
                else if (pendingAnimateMotionDepth >= 0 &&
                         reader.Depth == pendingAnimateMotionDepth + 1 &&
                         string.Equals(reader.LocalName, "mpath", StringComparison.Ordinal) &&
                         HasLocalMotionPathReference(reader))
                {
                    sawDeclarativeAnimation = true;
                    AddSmilTargetReference(reader, smilFragmentReferences, mustBePath: true);
                }

                ValidateInlineStyle(reader.GetAttribute("style"), displayPath);

                insideStyle = string.Equals(reader.LocalName, "style", StringComparison.Ordinal) && !reader.IsEmptyElement;
                if (insideStyle)
                    currentStyle = new StringBuilder();
            }
            ValidateSmilFragmentReferences(svgElements, smilFragmentReferences, displayPath);
            if (cssStyleBlocks.Count > 0)
            {
                var combinedCss = string.Join(Environment.NewLine, cssStyleBlocks);
                AddNames(cssKeyframeNames, WebVisualStoryCssAnimationValidator.GetKeyframeNames(combinedCss));
                AddNames(
                    cssAnimationNames,
                    WebVisualStoryCssAnimationValidator.GetEffectiveAnimationNamesForMatchingSelectors(
                        combinedCss,
                        svgElements.Select(static element => element.InlineStyle).ToArray(),
                        (selector, elementIndex) => MatchesSelector(selector, elementIndex, svgElements)));
            }
            else
            {
                foreach (var element in svgElements)
                    AddNames(cssAnimationNames, WebVisualStoryCssAnimationValidator.GetEffectiveAnimationNames(element.InlineStyle ?? string.Empty));
            }
            if (requireAnimation &&
                !sawDeclarativeAnimation &&
                !cssAnimationNames.Overlaps(cssKeyframeNames))
            {
                throw new InvalidOperationException(
                    $"Visual-story animated SVG artifact does not contain a supported animation: {displayPath}");
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a valid SVG: {displayPath}",
                ex);
        }
    }

    private static bool IsDeclarativeAnimationElement(XmlReader reader)
    {
        switch (reader.LocalName)
        {
            case "set":
                return reader.GetAttribute("attributeName") is { Length: > 0 } &&
                       reader.GetAttribute("to") is { Length: > 0 } &&
                       HasEffectiveSmilTiming(reader);
            case "animate":
            case "animateTransform":
                return reader.GetAttribute("attributeName") is { Length: > 0 } &&
                       HasEffectiveSmilTiming(reader) &&
                       HasAnimationValue(reader);
            case "animateMotion":
                return HasEffectiveSmilTiming(reader) &&
                       (reader.GetAttribute("path") is { Length: > 0 } || HasAnimationValue(reader));
            default:
                return false;
        }
    }

    private static bool HasLocalMotionPathReference(XmlReader reader)
    {
        var reference = reader.GetAttribute("href") ??
                        reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return reference is { Length: > 1 } && reference[0] == '#';
    }

    private static SvgElementIdentity ReadSvgElementIdentity(XmlReader reader, int parentIndex)
    {
        var classes = (reader.GetAttribute("class") ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new SvgElementIdentity(
            reader.LocalName,
            reader.GetAttribute("id"),
            classes,
            reader.GetAttribute("style"),
            parentIndex,
            reader.Depth);
    }

    private static int FindSvgParentIndex(IReadOnlyList<SvgElementIdentity> elements, int depth)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            if (elements[index].Depth < depth)
                return index;
        }
        return -1;
    }

    private static bool MatchesSelector(
        string selector,
        int elementIndex,
        IReadOnlyList<SvgElementIdentity> elements)
    {
        if (!TryParseSelectorChain(selector, out var parts, out var combinators) ||
            !MatchesSimpleSelector(parts[^1], elements[elementIndex]))
        {
            return false;
        }

        var currentIndex = elementIndex;
        for (var partIndex = parts.Count - 2; partIndex >= 0; partIndex--)
        {
            var parentIndex = elements[currentIndex].ParentIndex;
            if (combinators[partIndex] == '>')
            {
                if (parentIndex < 0 || !MatchesSimpleSelector(parts[partIndex], elements[parentIndex]))
                    return false;
                currentIndex = parentIndex;
                continue;
            }

            var matchedAncestor = -1;
            while (parentIndex >= 0)
            {
                if (MatchesSimpleSelector(parts[partIndex], elements[parentIndex]))
                {
                    matchedAncestor = parentIndex;
                    break;
                }
                parentIndex = elements[parentIndex].ParentIndex;
            }
            if (matchedAncestor < 0)
                return false;
            currentIndex = matchedAncestor;
        }
        return true;
    }

    private static bool TryParseSelectorChain(
        string selector,
        out List<string> parts,
        out List<char> combinators)
    {
        parts = new List<string>();
        combinators = new List<char>();
        var token = new StringBuilder();
        var pendingCombinator = '\0';
        for (var index = 0; index < selector.Length; index++)
        {
            var character = selector[index];
            if (character == '>' || char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    if (parts.Count > 0)
                        combinators.Add(pendingCombinator == '\0' ? ' ' : pendingCombinator);
                    parts.Add(token.ToString());
                    token.Clear();
                    pendingCombinator = '\0';
                }
                if (character == '>')
                    pendingCombinator = '>';
                else if (pendingCombinator == '\0' && parts.Count > 0)
                    pendingCombinator = ' ';
                continue;
            }
            if (pendingCombinator != '\0' && token.Length == 0 && parts.Count > combinators.Count)
            {
                combinators.Add(pendingCombinator);
                pendingCombinator = '\0';
            }
            token.Append(character);
        }
        if (token.Length > 0)
        {
            if (parts.Count > combinators.Count)
                combinators.Add(pendingCombinator == '\0' ? ' ' : pendingCombinator);
            parts.Add(token.ToString());
        }
        return parts.Count > 0 && combinators.Count == parts.Count - 1;
    }

    private static bool MatchesSimpleSelector(string selector, SvgElementIdentity element)
    {
        var token = selector.Trim();
        if (token.Length == 0 || token.Any(char.IsWhiteSpace))
            return false;

        var index = 0;
        if (token[index] == '*')
            index++;
        else if (token[index] is not ('.' or '#'))
        {
            var start = index;
            while (index < token.Length && IsSimpleCssIdentifierCharacter(token[index]))
                index++;
            if (index == start ||
                !string.Equals(token.Substring(start, index - start), element.LocalName, StringComparison.Ordinal))
                return false;
        }

        while (index < token.Length)
        {
            var prefix = token[index++];
            if (prefix is not ('.' or '#'))
                return false;
            var start = index;
            while (index < token.Length && IsSimpleCssIdentifierCharacter(token[index]))
                index++;
            if (index == start)
                return false;
            var value = token.Substring(start, index - start);
            if (prefix == '#')
            {
                if (!string.Equals(value, element.Id, StringComparison.Ordinal))
                    return false;
            }
            else if (!element.Classes.Contains(value, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static bool IsSimpleCssIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '-' or '_';

    private static void ValidateInlineStyle(string? css, string displayPath)
    {
        if (string.IsNullOrWhiteSpace(css))
            return;
        if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(css))
        {
            throw new InvalidOperationException(
                $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
        }
    }

    private static void AddNames(HashSet<string> destination, IReadOnlySet<string> source)
    {
        foreach (var name in source)
            destination.Add(name);
    }

    private static void ValidateSelfContainedReferences(XmlReader reader, string displayPath)
    {
        if (!reader.HasAttributes)
            return;
        if (!reader.MoveToFirstAttribute())
            return;
        do
        {
            var reference = reader.Value.Trim();
            if (string.Equals(reader.LocalName, "base", StringComparison.Ordinal) &&
                string.Equals(reader.NamespaceURI, "http://www.w3.org/XML/1998/namespace", StringComparison.Ordinal) &&
                reference.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG artifacts must be self-contained and cannot declare xml:base: {displayPath}");
            }
            var isDirectReference = string.Equals(reader.LocalName, "href", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(reader.LocalName, "src", StringComparison.OrdinalIgnoreCase);
            if (isDirectReference && reference.Length > 0 && reference[0] != '#' ||
                WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(reference))
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
            }
        } while (reader.MoveToNextAttribute());
        reader.MoveToElement();
    }

    private static bool HasEffectiveSmilTiming(XmlReader reader)
        => HasPositiveSmilDuration(reader.GetAttribute("dur")) &&
           HasAutomaticSmilBegin(reader.GetAttribute("begin")) &&
           HasActiveSmilRepeat(reader.GetAttribute("repeatCount"), reader.GetAttribute("repeatDur"));

    private static bool HasAutomaticSmilBegin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => token.Trim())
            .Any(static token => token == "0" || TryParseSmilClockValue(token, out var milliseconds) && milliseconds >= 0);
    }

    private static bool HasActiveSmilRepeat(string? repeatCount, string? repeatDuration)
    {
        if (!string.IsNullOrWhiteSpace(repeatCount) &&
            !string.Equals(repeatCount.Trim(), "indefinite", StringComparison.OrdinalIgnoreCase) &&
            (!double.TryParse(
                repeatCount.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var count) ||
             !double.IsFinite(count) ||
             count <= 0))
            return false;
        return string.IsNullOrWhiteSpace(repeatDuration) ||
               string.Equals(repeatDuration.Trim(), "indefinite", StringComparison.OrdinalIgnoreCase) ||
               HasPositiveSmilDuration(repeatDuration);
    }

    private static bool HasPositiveSmilDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return TryParseSmilClockValue(value.Trim(), out var milliseconds) && milliseconds > 0;
    }

    private static bool TryParseSmilClockValue(string token, out double milliseconds)
    {
        milliseconds = 0;
        if (token.EndsWith("ms", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(token[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMilliseconds))
        {
            milliseconds = parsedMilliseconds;
            return double.IsFinite(milliseconds);
        }
        var units = new[] { (Suffix: "min", Multiplier: 60d), (Suffix: "h", Multiplier: 3600d), (Suffix: "s", Multiplier: 1d) };
        foreach (var unit in units)
        {
            if (!token.EndsWith(unit.Suffix, StringComparison.OrdinalIgnoreCase) ||
                !double.TryParse(token[..^unit.Suffix.Length], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                continue;
            milliseconds = number * unit.Multiplier * 1000d;
            return double.IsFinite(milliseconds);
        }
        if (!TimeSpan.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out var duration))
            return false;
        milliseconds = duration.TotalMilliseconds;
        return double.IsFinite(milliseconds);
    }

}
