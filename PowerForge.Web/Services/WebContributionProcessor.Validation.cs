using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebContributionProcessor
{
    private static Dictionary<string, WebContributionAuthorProfile> LoadAuthors(string authorsRoot, List<string> warnings)
    {
        var authors = new Dictionary<string, WebContributionAuthorProfile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(authorsRoot))
        {
            warnings.Add($"Authors directory does not exist yet: {authorsRoot}");
            return authors;
        }

        foreach (var path in Directory.GetFiles(authorsRoot, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(static path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                                           path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var slug = Path.GetFileNameWithoutExtension(path);
            try
            {
                var map = AuthorDeserializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(path, Encoding.UTF8));
                if (map is null)
                    continue;

                var profile = MapAuthorProfile(map, slug, path);
                if (!string.IsNullOrWhiteSpace(profile.Slug))
                    authors[profile.Slug] = profile;
            }
            catch (Exception ex)
            {
                warnings.Add($"Author profile '{path}' could not be parsed as YAML: {ex.Message}");
            }
        }

        return authors;
    }

    private static WebContributionAuthorProfile MapAuthorProfile(Dictionary<string, object?> map, string fallbackSlug, string sourcePath)
    {
        return new WebContributionAuthorProfile
        {
            Name = ReadMapString(map, "name"),
            Slug = NormalizeSlug(ReadMapString(map, "slug")) ?? NormalizeSlug(fallbackSlug) ?? string.Empty,
            Title = NullIfWhiteSpace(ReadMapString(map, "title", "role")),
            Bio = NullIfWhiteSpace(ReadMapString(map, "bio", "description")),
            Avatar = NullIfWhiteSpace(ReadMapString(map, "avatar", "image")),
            X = NullIfWhiteSpace(ReadMapString(map, "x", "twitter")),
            LinkedIn = NullIfWhiteSpace(ReadMapString(map, "linkedin", "linkedIn")),
            GitHub = NullIfWhiteSpace(ReadMapString(map, "github", "gitHub")),
            Website = NullIfWhiteSpace(ReadMapString(map, "website", "url")),
            SourcePath = sourcePath
        };
    }

    private static void ValidateAuthorProfiles(
        IReadOnlyDictionary<string, WebContributionAuthorProfile> authors,
        WebContributionOptions options,
        List<string> errors)
    {
        foreach (var profile in authors.Values)
        {
            var label = string.IsNullOrWhiteSpace(profile.Slug) ? "author profile" : $"author '{profile.Slug}'";
            if (string.IsNullOrWhiteSpace(profile.Slug) || !SlugRegex.IsMatch(profile.Slug))
                errors.Add($"{label}: invalid slug. Use lowercase letters, numbers, and hyphens.");
            if (string.IsNullOrWhiteSpace(profile.Name))
                errors.Add($"{label}: missing name.");
            if (!IsEmptyOrValidHttpUrl(profile.LinkedIn, "linkedin.com"))
                errors.Add($"{label}: linkedin must be a valid linkedin.com URL.");
            if (!IsEmptyOrValidHttpUrl(profile.Website))
                errors.Add($"{label}: website must be a valid URL.");
            ValidateAuthorAvatar(profile, options, label, errors);
            if (!IsEmptyOrValidSocialValue(profile.X, allowUnderscoreHandle: true, "x.com", "twitter.com"))
                errors.Add($"{label}: x must be an X/Twitter URL or handle.");
            if (!IsEmptyOrValidSocialValue(profile.GitHub, allowUnderscoreHandle: false, "github.com"))
                errors.Add($"{label}: github must be a GitHub URL or username.");
        }
    }

    private static void ValidateAuthorAvatar(
        WebContributionAuthorProfile profile,
        WebContributionOptions options,
        string label,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(profile.Avatar))
            return;

        var avatar = profile.Avatar.Trim();
        if (IsEmptyOrValidHttpUrl(avatar) || IsRootedWebPath(avatar))
            return;

        if (string.IsNullOrWhiteSpace(profile.SourcePath))
        {
            errors.Add($"{label}: local avatar could not be resolved.");
            return;
        }

        var authorRoot = Path.GetDirectoryName(profile.SourcePath) ?? ".";
        if (!TryResolveAuthorAsset(authorRoot, avatar, out var fullPath))
        {
            errors.Add($"{label}: avatar '{avatar}' must stay inside authors/.");
            return;
        }

        profile.AvatarSourcePath = fullPath;
        if (!File.Exists(fullPath))
        {
            errors.Add($"{label}: avatar '{avatar}' does not exist.");
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!AllowedAssetExtensions.Contains(extension))
            errors.Add($"{label}: avatar '{avatar}' has unsupported extension '{extension}'. Use PNG, JPG, JPEG, WEBP, or GIF.");

        var length = new FileInfo(fullPath).Length;
        if (options.MaxAssetBytes > 0 && length > options.MaxAssetBytes)
            errors.Add($"{label}: avatar '{avatar}' is larger than {options.MaxAssetBytes} bytes.");
    }

    private static WebContributionPostResult ValidatePost(
        string indexPath,
        string postsRoot,
        IReadOnlyDictionary<string, WebContributionAuthorProfile> authors,
        WebContributionOptions options,
        List<string> errors,
        List<string> warnings)
    {
        var bundleRoot = Path.GetDirectoryName(indexPath) ?? postsRoot;
        var markdown = File.ReadAllText(indexPath, Encoding.UTF8);
        var (matter, body) = FrontMatterParser.Parse(markdown);
        var relative = ToSlash(Path.GetRelativePath(postsRoot, indexPath));
        var result = new WebContributionPostResult
        {
            SourcePath = indexPath,
            BundlePath = bundleRoot
        };

        if (matter is null)
        {
            errors.Add($"{relative}: missing YAML front matter.");
            return result;
        }

        result.Title = matter.Title?.Trim() ?? string.Empty;
        result.Language = ResolveLanguage(matter, postsRoot, indexPath);
        result.Slug = NormalizeSlug(matter.Slug) ?? NormalizeSlug(Path.GetFileName(bundleRoot)) ?? string.Empty;
        result.Authors = ReadStringList(matter.Meta, "authors", "author");
        result.Year = ResolveYear(matter.Date);
        ValidateBundleLayout(relative, result, errors);

        if (string.IsNullOrWhiteSpace(result.Title))
            errors.Add($"{relative}: missing front matter title.");
        if (string.IsNullOrWhiteSpace(matter.Description))
            errors.Add($"{relative}: missing front matter description.");
        if (matter.Date is null)
            errors.Add($"{relative}: missing front matter date.");
        else if (matter.Date.Value.Year is < 2000 or > 2100)
            errors.Add($"{relative}: front matter date year must be between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(result.Language))
            errors.Add($"{relative}: missing language. Put the post under posts/<language>/... or set language.");
        if (string.IsNullOrWhiteSpace(result.Slug) || !SlugRegex.IsMatch(result.Slug))
            errors.Add($"{relative}: invalid slug '{result.Slug}'. Use lowercase letters, numbers, and hyphens.");
        if (result.Authors.Length == 0)
            errors.Add($"{relative}: missing authors list.");

        foreach (var author in result.Authors)
        {
            if (!authors.TryGetValue(author, out var profile))
            {
                errors.Add($"{relative}: author '{author}' is not defined under authors/.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
                errors.Add($"{relative}: author '{author}' is missing a display name.");
        }

        result.AuthorNames = result.Authors
            .Select(author => authors.TryGetValue(author, out var profile) ? profile.Name : string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        var assets = Directory.GetFiles(bundleRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, indexPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.Assets = assets.Select(path => ToSlash(Path.GetRelativePath(bundleRoot, path))).ToArray();

        var totalBytes = 0L;
        foreach (var asset in assets)
        {
            var assetRelative = ToSlash(Path.GetRelativePath(bundleRoot, asset));
            var extension = Path.GetExtension(asset);
            if (!AllowedAssetExtensions.Contains(extension))
                errors.Add($"{relative}: asset '{assetRelative}' has unsupported extension '{extension}'. Use PNG, JPG, JPEG, WEBP, or GIF.");

            var length = new FileInfo(asset).Length;
            totalBytes += length;
            if (options.MaxAssetBytes > 0 && length > options.MaxAssetBytes)
                errors.Add($"{relative}: asset '{assetRelative}' is larger than {options.MaxAssetBytes} bytes.");
        }

        if (options.MaxPostAssetBytes > 0 && totalBytes > options.MaxPostAssetBytes)
            errors.Add($"{relative}: post assets total {totalBytes} bytes, above the {options.MaxPostAssetBytes} byte budget.");

        ValidateFeaturedImage(indexPath, relative, bundleRoot, matter, errors);
        ValidateMarkdownImages(relative, bundleRoot, body, errors, warnings);
        ValidateMarkdownEditorialHygiene(relative, body, warnings);
        return result;
    }

    private static void ValidateFeaturedImage(
        string indexPath,
        string relative,
        string bundleRoot,
        FrontMatter matter,
        List<string> errors)
    {
        if (!TryReadString(matter.Meta, "image", out var image) || string.IsNullOrWhiteSpace(image))
            return;

        if (IsExternalOrRootedWebPath(image))
            return;

        if (!TryResolveBundleAsset(bundleRoot, image, out var fullPath))
            errors.Add($"{relative}: featured image '{image}' must stay inside the post bundle.");
        else if (!File.Exists(fullPath))
            errors.Add($"{relative}: featured image '{image}' does not exist next to the post.");

        if (!TryReadString(matter.Meta, "image_alt", out var alt) &&
            !TryReadString(matter.Meta, "imageAlt", out alt) &&
            !TryReadString(matter.Meta, "alt", out alt))
        {
            errors.Add($"{relative}: featured image requires image_alt.");
        }
        else if (string.IsNullOrWhiteSpace(alt))
        {
            errors.Add($"{relative}: featured image_alt is empty.");
        }
        else if (LooksLikeSlugOrFileName(alt))
        {
            errors.Add($"{relative}: featured image_alt looks like a slug or file name. Describe what the image shows.");
        }
    }

    private static void ValidateMarkdownImages(
        string relative,
        string bundleRoot,
        string body,
        List<string> errors,
        List<string> warnings)
    {
        var scannableBody = MaskFencedCodeBlocks(body);
        foreach (Match match in MarkdownImageRegex.Matches(scannableBody))
        {
            var alt = match.Groups["alt"].Value;
            var target = UnescapeMarkdownTarget(match.Groups["target"].Value);
            if (string.IsNullOrWhiteSpace(alt))
                errors.Add($"{relative}: markdown image '{target}' is missing alt text.");

            if (IsExternalOrRootedWebPath(target))
                continue;

            if (!TryResolveBundleAsset(bundleRoot, target, out var fullPath))
                errors.Add($"{relative}: markdown image target '{target}' must stay inside the post bundle.");
            else if (!File.Exists(fullPath))
                errors.Add($"{relative}: markdown image target '{target}' does not exist.");
        }

        if (scannableBody.Contains("<img", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{relative}: contains raw <img> HTML. Prefer Markdown image syntax so validation can check alt text and local files.");
    }

    private static void ValidateMarkdownEditorialHygiene(
        string relative,
        string body,
        List<string> warnings)
    {
        var scannableBody = MaskFencedCodeBlocks(body);
        if (ContainsDecorativeSeparator(scannableBody))
            warnings.Add($"{relative}: contains decorative separator lines. Prefer headings, paragraphs, or Markdown lists.");

        // Use the unmasked body here because the anti-pattern is the label immediately before a fence.
        var bareFenceLabelMatch = BareFenceLanguageLabelRegex.Match(body);
        if (bareFenceLabelMatch.Success)
        {
            var label = bareFenceLabelMatch.Groups["label"].Value;
            warnings.Add($"{relative}: contains standalone '{label}' before a fenced code block. Put the language on the opening fence, for example ```{label}.");
        }

        if (scannableBody.Contains('•'))
            warnings.Add($"{relative}: contains bullet characters. Prefer Markdown list markers such as '-'.");

        WarnIfContains(scannableBody, "PowerApps", "Power Apps", relative, warnings);
        WarnIfContains(scannableBody, "PowerAutomate", "Power Automate", relative, warnings);
        WarnIfContains(scannableBody, "Sharepoint", "SharePoint", relative, warnings);
    }

    private static bool ContainsDecorativeSeparator(string body)
    {
        foreach (Match match in DecorativeSeparatorRegex.Matches(body))
        {
            if (!IsLikelySetextHeadingUnderline(body, match))
                return true;
        }

        return false;
    }

    private static bool IsLikelySetextHeadingUnderline(string body, Match separatorMatch)
    {
        var markerGroup = separatorMatch.Groups["marker"];
        if (!markerGroup.Success)
            return false;

        var marker = markerGroup.Value;
        if (string.IsNullOrWhiteSpace(marker) || marker[0] == '_')
            return false;

        var previousLineEnd = separatorMatch.Index - 1;
        while (previousLineEnd >= 0 && (body[previousLineEnd] == '\r' || body[previousLineEnd] == '\n'))
            previousLineEnd--;
        if (previousLineEnd < 0)
            return false;

        var previousLineStart = body.LastIndexOf('\n', previousLineEnd);
        previousLineStart = previousLineStart < 0 ? 0 : previousLineStart + 1;
        var previousLine = body.Substring(previousLineStart, previousLineEnd - previousLineStart + 1).Trim();
        if (previousLine.Length == 0)
            return false;

        if (previousLine.StartsWith("#", StringComparison.Ordinal) ||
            previousLine.StartsWith(">", StringComparison.Ordinal) ||
            previousLine.StartsWith("|", StringComparison.Ordinal) ||
            previousLine.StartsWith("-", StringComparison.Ordinal) ||
            previousLine.StartsWith("*", StringComparison.Ordinal) ||
            previousLine.StartsWith("+", StringComparison.Ordinal) ||
            previousLine.StartsWith("`", StringComparison.Ordinal) ||
            previousLine.StartsWith("~", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeSlugOrFileName(string value)
    {
        var text = value.Trim();
        if (text.Length < 8)
            return false;

        if (text.Contains(' ', StringComparison.Ordinal))
            return false;

        if (Path.HasExtension(text))
            return true;

        return SlugLikeAltTextRegex.IsMatch(text);
    }

    private static void WarnIfContains(
        string body,
        string current,
        string preferred,
        string relative,
        List<string> warnings)
    {
        if (body.Contains(current, StringComparison.Ordinal))
            warnings.Add($"{relative}: contains '{current}'. Prefer '{preferred}' in visible article copy.");
    }
}
