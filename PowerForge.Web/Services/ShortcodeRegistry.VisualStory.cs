using System.Text;

namespace PowerForge.Web;

internal static partial class ShortcodeDefaults
{
    internal static string RenderVisualStory(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureMediaBaseCss(context);

        var manifestValue = ReadAttr(attrs, "manifest", "src", "bundle");
        if (string.IsNullOrWhiteSpace(manifestValue))
            throw new InvalidOperationException("story shortcode requires a manifest.");

        var manifestPath = ResolveVisualStoryManifestPath(context.RootPath, manifestValue);
        var bundle = context.VisualStoryCache.Load(manifestPath);
        var baseUrl = ReadAttr(attrs, "base", "baseUrl", "base-url");
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DeriveVisualStoryBaseUrl(context, manifestPath);

        var animated = SelectStoryArtifact(bundle, "animated", "svg") ??
                       SelectStoryArtifact(bundle, "animated", "gif") ??
                       SelectStoryArtifact(bundle, "animated", "apng");
        var completed = SelectStoryArtifact(bundle, "completed", "png")
                        ?? throw new InvalidOperationException(
                            $"Visual-story bundle '{bundle.Id}' has no completed PNG artifact.");
        var transcript = SelectStoryArtifact(bundle, "transcript");
        var mode = ReadAttr(attrs, "mode", "format").Trim().ToLowerInvariant();
        var requested = mode switch
        {
            "gif" => SelectStoryArtifact(bundle, "animated", "gif"),
            "apng" => SelectStoryArtifact(bundle, "animated", "apng"),
            "completed" or "png" or "static" => completed,
            _ => animated
        };
        requested ??= completed;

        var title = ReadAttr(attrs, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = bundle.Title;
        var alt = ReadAttr(attrs, "alt");
        if (string.IsNullOrWhiteSpace(alt))
            alt = bundle.Alt;
        var caption = ReadAttr(attrs, "caption");
        if (string.IsNullOrWhiteSpace(caption))
            caption = bundle.Caption ?? bundle.Outcome;
        var loading = NormalizeLoading(ReadAttr(attrs, "loading"), "lazy");
        var transcriptMode = ReadAttr(attrs, "transcript").Trim().ToLowerInvariant();
        var showDownloads = ReadBoolAttr(attrs, defaultValue: true, "downloads", "showDownloads");
        var className = JoinClassTokens("pf-story", ReadAttr(attrs, "class"), ResolveSizeClass(attrs, "xl"));
        var style = BuildContainerStyle(attrs, "xl", "center");

        var requestedUrl = JoinVisualStoryUrl(baseUrl, requested.Path);
        var completedUrl = JoinVisualStoryUrl(baseUrl, completed.Path);
        var completedSrcSetUrl = SrcSetUrlEncoder.Encode(completedUrl);
        var sb = new StringBuilder();
        sb.AppendLine($@"<figure class=""{System.Web.HttpUtility.HtmlEncode(className)}"" data-pf-story=""{System.Web.HttpUtility.HtmlEncode(bundle.Id)}"" style=""{System.Web.HttpUtility.HtmlEncode(style)}"">");
        sb.AppendLine(@"  <div class=""pf-story-frame"">");
        if (!ReferenceEquals(requested, completed))
        {
            sb.AppendLine("    <picture>");
            sb.AppendLine($@"      <source media=""print"" srcset=""{System.Web.HttpUtility.HtmlEncode(completedSrcSetUrl)}"" type=""image/png"" />");
            sb.AppendLine($@"      <source media=""(prefers-reduced-motion: reduce)"" srcset=""{System.Web.HttpUtility.HtmlEncode(completedSrcSetUrl)}"" type=""image/png"" />");
            sb.AppendLine($@"      <img src=""{System.Web.HttpUtility.HtmlEncode(requestedUrl)}"" alt=""{System.Web.HttpUtility.HtmlEncode(alt)}"" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}"" decoding=""async"" />");
            sb.AppendLine("    </picture>");
        }
        else
        {
            sb.AppendLine($@"    <img src=""{System.Web.HttpUtility.HtmlEncode(completedUrl)}"" alt=""{System.Web.HttpUtility.HtmlEncode(alt)}"" loading=""{System.Web.HttpUtility.HtmlEncode(loading)}"" decoding=""async"" />");
        }
        sb.AppendLine("  </div>");
        sb.AppendLine(@"  <figcaption class=""pf-story-caption"">");
        sb.AppendLine($@"    <strong>{System.Web.HttpUtility.HtmlEncode(title)}</strong>");
        sb.AppendLine($@"    <span>{System.Web.HttpUtility.HtmlEncode(caption)}</span>");
        sb.AppendLine("  </figcaption>");

        if (transcript is not null && transcriptMode is not "hidden" and not "off" and not "false")
        {
            var open = transcriptMode is "expanded" or "open" ? " open" : string.Empty;
            var transcriptPath = ResolveVisualStoryArtifactPath(manifestPath, transcript.Path);
            var transcriptText = File.ReadAllText(transcriptPath);
            sb.AppendLine($@"  <details class=""pf-story-transcript""{open}>");
            sb.AppendLine("    <summary>Accessible transcript</summary>");
            sb.AppendLine($@"    <pre>{System.Web.HttpUtility.HtmlEncode(transcriptText)}</pre>");
            sb.AppendLine("  </details>");
        }

        if (showDownloads)
        {
            sb.AppendLine(@"  <div class=""pf-story-actions"" aria-label=""Story downloads"">");
            foreach (var artifact in bundle.Artifacts.Where(static artifact =>
                         string.Equals(artifact.Role, "animated", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(artifact.Role, "completed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(artifact.Role, "transcript", StringComparison.OrdinalIgnoreCase)))
            {
                var url = JoinVisualStoryUrl(baseUrl, artifact.Path);
                var label = artifact.Role == "completed"
                    ? "Completed PNG"
                    : artifact.Format.ToUpperInvariant();
                sb.AppendLine($@"    <a href=""{System.Web.HttpUtility.HtmlEncode(url)}"" download>{System.Web.HttpUtility.HtmlEncode(label)}</a>");
            }
            sb.AppendLine("  </div>");
        }

        sb.Append("</figure>");
        return sb.ToString();
    }

    private static WebVisualStoryArtifact? SelectStoryArtifact(
        WebVisualStoryBundle bundle,
        string role,
        string? format = null)
        => bundle.Artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.Role, role, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(format) ||
             string.Equals(artifact.Format, format, StringComparison.OrdinalIgnoreCase)));

    private static string ResolveVisualStoryManifestPath(string rootPath, string manifest)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(rootPath) ? "." : rootPath);
        return VisualStoryPathGuard.ResolveRelativePath(root, manifest, "shortcode manifest");
    }

    private static string ResolveVisualStoryArtifactPath(string manifestPath, string artifactPath)
    {
        var root = Path.GetDirectoryName(manifestPath)
                   ?? throw new InvalidOperationException("Visual-story manifest has no parent directory.");
        return VisualStoryPathGuard.ResolveRelativePath(root, artifactPath, "shortcode artifact");
    }

    private static string DeriveVisualStoryBaseUrl(ShortcodeRenderContext context, string manifestPath)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(context.RootPath) ? "." : context.RootPath);
        var publishedPath = ResolvePublishedVisualStoryManifestPath(context.Site, root, manifestPath);
        if (publishedPath is null &&
            TryResolvePageBundleVisualStoryBaseUrl(context, manifestPath, out var pageBundleBaseUrl))
            return pageBundleBaseUrl;
        var normalized = publishedPath ?? Path.GetRelativePath(root, manifestPath).Replace('\\', '/');
        var directory = normalized.Contains('/')
            ? normalized[..normalized.LastIndexOf('/')]
            : string.Empty;
        if (publishedPath is null &&
            normalized.StartsWith("static/", StringComparison.OrdinalIgnoreCase) &&
            directory.Equals("static", StringComparison.OrdinalIgnoreCase))
            directory = string.Empty;
        else if (publishedPath is null &&
                 normalized.StartsWith("static/", StringComparison.OrdinalIgnoreCase) &&
                 directory.StartsWith("static/", StringComparison.OrdinalIgnoreCase))
            directory = directory["static/".Length..];
        var encodedDirectory = string.Join(
            "/",
            directory.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return "/" + encodedDirectory;
    }

    private static bool TryResolvePageBundleVisualStoryBaseUrl(
        ShortcodeRenderContext context,
        string manifestPath,
        out string baseUrl)
    {
        baseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(context.PageResourceBaseUrl) ||
            string.IsNullOrWhiteSpace(context.SourcePath))
            return false;

        var bundleRoot = Path.GetDirectoryName(Path.GetFullPath(context.SourcePath));
        if (string.IsNullOrWhiteSpace(bundleRoot) ||
            !TryGetContainedRelativePath(bundleRoot, manifestPath, out var relativeManifestPath))
            return false;

        var directory = relativeManifestPath.Contains('/')
            ? relativeManifestPath[..relativeManifestPath.LastIndexOf('/')]
            : string.Empty;
        var encodedDirectory = string.Join(
            "/",
            directory.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        var pageBase = "/" + context.PageResourceBaseUrl.Trim('/');
        baseUrl = encodedDirectory.Length == 0
            ? pageBase
            : pageBase.TrimEnd('/') + "/" + encodedDirectory;
        return true;
    }

    private static string? ResolvePublishedVisualStoryManifestPath(
        SiteSpec site,
        string root,
        string manifestPath)
    {
        var mappings = site.StaticAssets ?? Array.Empty<StaticAssetSpec>();
        var candidates = new List<(int Specificity, string PublishedPath)>();
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Source))
                continue;

            var sourcePath = Path.GetFullPath(
                Path.IsPathRooted(mapping.Source)
                    ? mapping.Source
                    : Path.Combine(root, mapping.Source));
            var destination = (mapping.Destination ?? string.Empty)
                .TrimStart('/', '\\')
                .Replace('\\', '/');
            if (File.Exists(sourcePath))
            {
                // A file mapping publishes only the manifest. It cannot establish a URL
                // base for the sibling artifacts referenced by that manifest.
                continue;
            }

            if (!TryGetContainedRelativePath(sourcePath, manifestPath, out var relative))
                continue;
            var published = string.IsNullOrWhiteSpace(destination)
                ? relative
                : destination.TrimEnd('/') + "/" + relative;
            candidates.Add((sourcePath.Length, published));
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Specificity)
            .Select(static candidate => candidate.PublishedPath)
            .FirstOrDefault();
    }

    internal static bool TryGetContainedRelativePath(
        string root,
        string path,
        out string relativePath,
        StringComparison? comparison = null)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var pathComparison = comparison ?? WebVisualStoryStager.GetFileSystemPathComparison(fullRoot);
        if (string.Equals(fullRoot, fullPath, pathComparison))
        {
            relativePath = string.Empty;
            return false;
        }

        var prefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, pathComparison))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = fullPath.Substring(prefix.Length).Replace('\\', '/');
        return relativePath.Length > 0 && !Path.IsPathRooted(relativePath);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string JoinVisualStoryUrl(string baseUrl, string path)
    {
        var encodedPath = string.Join(
            "/",
            path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return baseUrl.TrimEnd('/') + "/" + encodedPath;
    }

}
