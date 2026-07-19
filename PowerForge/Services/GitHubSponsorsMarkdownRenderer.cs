using System.Net;
using System.Text;

namespace PowerForge;

/// <summary>
/// Renders normalized Sponsors data as deterministic Markdown and safe inline HTML.
/// </summary>
public sealed class GitHubSponsorsMarkdownRenderer
{
    /// <summary>
    /// Renders one Sponsors output block.
    /// </summary>
    /// <param name="recognition">Prepared sponsor records and recognition tiers.</param>
    /// <param name="output">Output layout settings.</param>
    /// <returns>Generated Markdown.</returns>
    public string Render(GitHubSponsorRecognitionResult recognition, GitHubSponsorsOutputSpec output)
    {
        if (recognition is null) throw new ArgumentNullException(nameof(recognition));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var current = (recognition.Sponsors ?? Array.Empty<GitHubSponsorRecord>())
            .Where(sponsor => sponsor.Status == GitHubSponsorStatus.Current)
            .ToArray();
        var former = (recognition.Sponsors ?? Array.Empty<GitHubSponsorRecord>())
            .Where(sponsor => sponsor.Status == GitHubSponsorStatus.Former)
            .ToArray();

        return output.Layout switch
        {
            GitHubSponsorsMarkdownLayout.Compact => RenderCompact(current, output),
            _ => RenderFull(current, former, recognition, output)
        };
    }

    private static string RenderFull(
        GitHubSponsorRecord[] current,
        GitHubSponsorRecord[] former,
        GitHubSponsorRecognitionResult recognition,
        GitHubSponsorsOutputSpec output)
    {
        var builder = new StringBuilder();
        AppendOptionalMarkdown(builder, output.Introduction);

        if (current.Length == 0)
        {
            builder.AppendLine("No public sponsors are currently listed.");
            builder.AppendLine();
        }
        else if (recognition.TierRecognitionEnabled)
        {
            foreach (var tier in recognition.Tiers ?? Array.Empty<GitHubSponsorRecognitionTierSpec>())
            {
                var members = current
                    .Where(sponsor => string.Equals(sponsor.RecognitionTierKey, tier.Key, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (members.Length == 0)
                    continue;

                AppendSectionHeading(builder, tier.Heading);
                AppendSponsorRow(builder, members, tier.AvatarSize);
            }
        }
        else
        {
            AppendSectionHeading(builder, "Current Sponsors");
            AppendSponsorRow(builder, current, output.AvatarSize);
        }

        if (output.IncludeFormer && former.Length > 0)
        {
            AppendSectionHeading(builder, "Past Sponsors");
            builder.AppendLine("We are grateful to everyone who has supported this work.");
            builder.AppendLine();
            builder.AppendLine("<ul>");
            foreach (var sponsor in former)
            {
                var name = WebUtility.HtmlEncode((sponsor.DisplayName ?? sponsor.Key).Replace('\r', ' ').Replace('\n', ' ').Trim());
                var profile = NormalizeAbsoluteUrl(sponsor.ProfileUrl);
                builder.Append("  <li>");
                if (profile is null)
                    builder.Append(name);
                else
                    builder.Append("<a href=\"").Append(WebUtility.HtmlEncode(profile)).Append("\">").Append(name).Append("</a>");
                builder.AppendLine("</li>");
            }
            builder.AppendLine("</ul>");
            builder.AppendLine();
        }

        AppendOptionalMarkdown(builder, output.Closing);
        return builder.ToString().TrimEnd();
    }

    private static string RenderCompact(GitHubSponsorRecord[] current, GitHubSponsorsOutputSpec output)
    {
        var builder = new StringBuilder();
        AppendOptionalMarkdown(builder, output.Introduction);

        var maximum = Math.Max(0, output.MaxEntries);
        var members = maximum == 0 ? current : current.Take(maximum).ToArray();
        if (members.Length > 0)
            AppendSponsorRow(builder, members, output.AvatarSize);
        else
        {
            builder.AppendLine("No public sponsors are currently listed.");
            builder.AppendLine();
        }

        var moreLink = NormalizeRelativeOrAbsoluteUrl(output.MoreLink);
        if (moreLink is not null)
        {
            builder.Append("[See all sponsors](").Append(EscapeMarkdownUrl(moreLink)).AppendLine(")");
            builder.AppendLine();
        }

        AppendOptionalMarkdown(builder, output.Closing);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSponsorRow(StringBuilder builder, GitHubSponsorRecord[] sponsors, int requestedAvatarSize)
    {
        var avatarSize = Math.Max(24, Math.Min(256, requestedAvatarSize));
        builder.AppendLine("<p>");
        foreach (var sponsor in sponsors)
        {
            var displayName = WebUtility.HtmlEncode(sponsor.DisplayName ?? sponsor.Key);
            var profileUrl = NormalizeAbsoluteUrl(sponsor.ProfileUrl);
            var avatarUrl = NormalizeAbsoluteUrl(sponsor.AvatarUrl);

            if (avatarUrl is not null)
            {
                if (profileUrl is not null)
                    builder.Append("  <a href=\"").Append(WebUtility.HtmlEncode(profileUrl)).Append("\" title=\"").Append(displayName).Append("\">");
                else
                    builder.Append("  ");

                builder.Append("<img src=\"").Append(WebUtility.HtmlEncode(avatarUrl))
                    .Append("\" width=\"").Append(avatarSize)
                    .Append("\" height=\"").Append(avatarSize)
                    .Append("\" alt=\"").Append(displayName).Append("\" />");
                if (profileUrl is not null)
                    builder.Append("</a>");
                builder.AppendLine();
                continue;
            }

            builder.Append("  ");
            if (profileUrl is null) builder.Append(displayName); else builder.Append("<a href=\"").Append(WebUtility.HtmlEncode(profileUrl)).Append("\">").Append(displayName).Append("</a>");
            builder.AppendLine("<br />");
        }
        builder.AppendLine("</p>");
        builder.AppendLine();
    }

    private static void AppendSectionHeading(StringBuilder builder, string? heading)
    {
        var normalized = (heading ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        builder.Append("## ").AppendLine(normalized.Length == 0 ? "Sponsors" : normalized);
        builder.AppendLine();
    }

    private static void AppendOptionalMarkdown(StringBuilder builder, string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return;
        builder.AppendLine(markdown!.Replace("\r\n", "\n").Replace('\r', '\n').Trim());
        builder.AppendLine();
    }

    private static string EscapeMarkdownUrl(string value)
        => value.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    private static string? NormalizeAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
            return null;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    private static string? NormalizeRelativeOrAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value!.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            return NormalizeAbsoluteUrl(trimmed);
        if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("/", StringComparison.Ordinal) || !trimmed.Contains(':'))
            return trimmed;
        return null;
    }
}
