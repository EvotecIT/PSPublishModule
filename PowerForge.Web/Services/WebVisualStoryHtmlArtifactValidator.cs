using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Validates that an HTML story artifact depends only on declared files in its bundle.</summary>
internal static class WebVisualStoryHtmlArtifactValidator
{
    private static readonly HashSet<string> ActiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "animate", "animateMotion", "animateTransform", "applet", "discard", "embed", "frame",
        "frameset", "iframe", "object", "portal", "script", "set"
    };
    private static readonly HashSet<string> ResourceHrefElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "feImage", "image", "use"
    };
    private static readonly HashSet<string> ResourceLinkRelations = new(StringComparer.OrdinalIgnoreCase)
    {
        "apple-touch-icon", "dns-prefetch", "icon", "manifest", "mask-icon", "modulepreload",
        "preconnect", "prefetch", "preload", "prerender", "stylesheet"
    };

    internal static void Validate(
        string html,
        string displayPath,
        string bundleRoot,
        IReadOnlySet<string> declaredArtifactPaths)
    {
        var document = HtmlParser.ParseWithAngleSharp(html);
        if (document.QuerySelector("base[href]") is not null)
            throw Invalid(displayPath, "base URLs are not allowed");

        foreach (var element in document.All)
        {
            ValidatePassiveContent(element, displayPath);
            ValidateSvgPresentationAttributes(element, displayPath);
            ValidateAttribute(element, "src", displayPath, bundleRoot, declaredArtifactPaths);
            ValidateAttribute(element, "background", displayPath, bundleRoot, declaredArtifactPaths);
            if (string.Equals(element.LocalName, "video", StringComparison.OrdinalIgnoreCase))
                ValidateAttribute(element, "poster", displayPath, bundleRoot, declaredArtifactPaths);
            if (string.Equals(element.LocalName, "object", StringComparison.OrdinalIgnoreCase))
                ValidateAttribute(element, "data", displayPath, bundleRoot, declaredArtifactPaths);

            if (ResourceHrefElements.Contains(element.LocalName))
            {
                var reference = element.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(reference))
                    ValidateReference(reference, displayPath, bundleRoot, declaredArtifactPaths);
                var legacyReference = element.GetAttribute("xlink:href");
                if (!string.IsNullOrWhiteSpace(legacyReference))
                    ValidateReference(legacyReference, displayPath, bundleRoot, declaredArtifactPaths);
            }
            if (string.Equals(element.LocalName, "link", StringComparison.OrdinalIgnoreCase) &&
                HasResourceLinkRelation(element.GetAttribute("rel")))
            {
                ValidateAttribute(element, "href", displayPath, bundleRoot, declaredArtifactPaths);
            }

            foreach (var sourceSetAttribute in new[] { "srcset", "imagesrcset" })
            {
                var sourceSet = element.GetAttribute(sourceSetAttribute);
                if (string.IsNullOrWhiteSpace(sourceSet))
                    continue;
                foreach (var reference in ParseSourceSet(sourceSet))
                    ValidateReference(reference, displayPath, bundleRoot, declaredArtifactPaths);
            }

            var inlineStyle = element.GetAttribute("style");
            if (!string.IsNullOrWhiteSpace(inlineStyle) &&
                WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(inlineStyle))
            {
                throw Invalid(displayPath, "inline CSS cannot reference external or sibling resources");
            }

            if (element.HasAttribute("srcdoc"))
                throw Invalid(displayPath, "iframe srcdoc content is not allowed");
            if (string.Equals(element.LocalName, "meta", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.GetAttribute("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(displayPath, "meta refresh navigation is not allowed");
            }
        }

        foreach (var style in document.QuerySelectorAll("style"))
        {
            if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(style.TextContent))
                throw Invalid(displayPath, "embedded CSS cannot reference external or sibling resources");
        }
    }

    private static void ValidatePassiveContent(IElement element, string displayPath)
    {
        if (ActiveElements.Contains(element.LocalName))
        {
            throw Invalid(displayPath, $"active <{element.LocalName}> content is not allowed");
        }

        foreach (var attribute in element.Attributes)
        {
            if (attribute.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(displayPath, $"event-handler attribute is not allowed: {attribute.LocalName}");
            }
        }

        if (string.Equals(element.LocalName, "link", StringComparison.OrdinalIgnoreCase) &&
            HasLinkRelation(element.GetAttribute("rel"), "stylesheet"))
        {
            throw Invalid(displayPath, "external stylesheets are not allowed; use validated embedded CSS");
        }

        ValidatePassiveNavigation(element, "href", displayPath);
        ValidatePassiveNavigation(element, "action", displayPath);
        ValidatePassiveNavigation(element, "formaction", displayPath);
    }

    private static void ValidatePassiveNavigation(IElement element, string attributeName, string displayPath)
    {
        var value = System.Net.WebUtility.HtmlDecode(element.GetAttribute(attributeName) ?? string.Empty).Trim();
        if (value.Any(static character => character <= '\u001f' || character == '\u007f'))
        {
            throw Invalid(displayPath, $"active navigation is not allowed: {attributeName}");
        }
        if (value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(displayPath, $"active navigation is not allowed: {attributeName}");
        }
    }

    private static void ValidateSvgPresentationAttributes(IElement element, string displayPath)
    {
        if (!string.Equals(element.NamespaceUri, "http://www.w3.org/2000/svg", StringComparison.Ordinal))
            return;

        foreach (var attribute in element.Attributes)
        {
            if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(attribute.Value))
            {
                throw Invalid(displayPath, "embedded SVG presentation attributes cannot reference external or sibling resources");
            }
        }
    }

    private static void ValidateAttribute(
        IElement element,
        string attributeName,
        string displayPath,
        string bundleRoot,
        IReadOnlySet<string> declaredArtifactPaths)
    {
        var reference = element.GetAttribute(attributeName);
        if (!string.IsNullOrWhiteSpace(reference))
            ValidateReference(reference, displayPath, bundleRoot, declaredArtifactPaths);
    }

    private static bool HasResourceLinkRelation(string? relation)
        => !string.IsNullOrWhiteSpace(relation) &&
           relation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
               .Any(ResourceLinkRelations.Contains);

    private static bool HasLinkRelation(string? relation, string expected)
        => !string.IsNullOrWhiteSpace(relation) &&
           relation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
               .Any(token => string.Equals(token, expected, StringComparison.OrdinalIgnoreCase));

    private static void ValidateReference(
        string rawReference,
        string htmlArtifactPath,
        string bundleRoot,
        IReadOnlySet<string> declaredArtifactPaths)
    {
        var reference = System.Net.WebUtility.HtmlDecode(rawReference).Trim();
        if (reference.Length == 0 || reference[0] == '#' ||
            reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (reference.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(reference, UriKind.Absolute, out _))
        {
            throw Invalid(htmlArtifactPath, $"external resource reference is not allowed: {reference}");
        }

        var pathOnly = reference.Split(['?', '#'], 2)[0];
        if (pathOnly.StartsWith("/", StringComparison.Ordinal) || pathOnly.StartsWith('\\'))
            throw Invalid(htmlArtifactPath, $"site-root resource reference is not allowed: {reference}");

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(pathOnly).Replace('\\', '/');
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story HTML artifact contains an invalid resource reference: {htmlArtifactPath}",
                ex);
        }

        var htmlDirectory = Path.GetDirectoryName(htmlArtifactPath.Replace('/', Path.DirectorySeparatorChar))
                            ?? string.Empty;
        var relativeReference = Path.Combine(htmlDirectory, decodedPath.Replace('/', Path.DirectorySeparatorChar));
        string resolvedPath;
        try
        {
            resolvedPath = VisualStoryPathGuard.ResolveRelativePath(
                bundleRoot,
                relativeReference,
                "HTML story dependency");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"Visual-story HTML artifact must be self-contained (resource escapes its bundle): {htmlArtifactPath} -> {reference}",
                ex);
        }
        var portablePath = Path.GetRelativePath(bundleRoot, resolvedPath).Replace('\\', '/');
        if (!declaredArtifactPaths.Contains(portablePath))
        {
            throw Invalid(
                htmlArtifactPath,
                $"resource must resolve to a declared bundle artifact: {reference}");
        }
    }

    private static IEnumerable<string> ParseSourceSet(string sourceSet)
    {
        var index = 0;
        while (index < sourceSet.Length)
        {
            while (index < sourceSet.Length &&
                   (char.IsWhiteSpace(sourceSet[index]) || sourceSet[index] == ','))
            {
                index++;
            }
            if (index >= sourceSet.Length)
            {
                yield break;
            }

            var start = index;
            var isData = sourceSet.AsSpan(index).StartsWith("data:", StringComparison.OrdinalIgnoreCase);
            while (index < sourceSet.Length &&
                   !char.IsWhiteSpace(sourceSet[index]) &&
                   (isData || sourceSet[index] != ','))
            {
                index++;
            }

            var reference = sourceSet[start..index].Trim();
            if (reference.Length > 0)
            {
                yield return reference;
            }

            while (index < sourceSet.Length && char.IsWhiteSpace(sourceSet[index]))
            {
                index++;
            }
            if (isData && reference.EndsWith(",", StringComparison.Ordinal) &&
                index < sourceSet.Length && !LooksLikeSourceSetDescriptor(sourceSet, index))
            {
                continue;
            }
            if (index < sourceSet.Length && sourceSet[index] == ',')
            {
                index++;
                continue;
            }

            while (index < sourceSet.Length && sourceSet[index] != ',')
            {
                index++;
            }
            if (index < sourceSet.Length)
            {
                index++;
            }
        }
    }

    private static bool LooksLikeSourceSetDescriptor(string sourceSet, int start)
    {
        var end = start;
        while (end < sourceSet.Length &&
               !char.IsWhiteSpace(sourceSet[end]) &&
               sourceSet[end] != ',')
        {
            end++;
        }
        if (end - start < 2)
        {
            return false;
        }

        var suffix = sourceSet[end - 1];
        if (suffix is not ('w' or 'x' or 'h'))
        {
            return false;
        }
        return double.TryParse(
            sourceSet.AsSpan(start, end - start - 1),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value > 0 && double.IsFinite(value);
    }

    private static InvalidOperationException Invalid(string displayPath, string reason)
        => new($"Visual-story HTML artifact must be self-contained ({reason}): {displayPath}");
}
