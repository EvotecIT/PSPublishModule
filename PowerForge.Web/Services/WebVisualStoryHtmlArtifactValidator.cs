using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Validates that an HTML story artifact depends only on declared files in its bundle.</summary>
internal static class WebVisualStoryHtmlArtifactValidator
{
    private static readonly HashSet<string> ResourceHrefElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "use"
    };
    private static readonly HashSet<string> ResourceLinkRelations = new(StringComparer.OrdinalIgnoreCase)
    {
        "icon", "manifest", "modulepreload", "preload", "stylesheet"
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
            ValidateAttribute(element, "src", displayPath, bundleRoot, declaredArtifactPaths);
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

            var sourceSet = element.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(sourceSet))
            {
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
        if (sourceSet.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            yield return sourceSet.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            yield break;
        }
        foreach (var candidate in sourceSet.Split(','))
        {
            var parts = candidate.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            var reference = parts[0];
            if (reference.Length > 0)
                yield return reference;
        }
    }

    private static InvalidOperationException Invalid(string displayPath, string reason)
        => new($"Visual-story HTML artifact must be self-contained ({reason}): {displayPath}");
}
