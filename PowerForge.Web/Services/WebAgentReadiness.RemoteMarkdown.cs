using System.Net.Http;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private static async Task<RemoteMarkdownArtifactScan> ScanRemoteMarkdownArtifactAsync(
        HttpClient http,
        string baseUrl,
        string linkHeader,
        AgentReadinessSpec spec,
        bool markdownNegotiated,
        CancellationToken cancellationToken)
    {
        var expected = spec.MarkdownArtifacts?.Enabled == true;
        var url = expected
            ? ResolveRemoteOutputUrl(
                baseUrl,
                "index" + NormalizeMarkdownExtension(spec.MarkdownArtifacts!.Extension),
                "/index.md")
            : ResolveMarkdownAlternateUrl(baseUrl, linkHeader) ?? CombineUrl(baseUrl, "/index.md");
        var result = expected || !markdownNegotiated
            ? await TryGetTextAsync(http, url, null, cancellationToken).ConfigureAwait(false)
            : null;
        var contentType = result?.Response?.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var fetched = result?.Success == true;
        return new RemoteMarkdownArtifactScan(
            expected,
            url,
            result,
            contentType,
            fetched,
            fetched && IsMarkdownContentType(contentType));
    }

    private static void AddRemoteMarkdownArtifactCheck(
        List<WebAgentReadinessCheck> checks,
        RemoteMarkdownArtifactScan artifact,
        bool markdownNegotiated)
    {
        if (!artifact.Expected && markdownNegotiated)
            return;

        AddCheck(checks, "markdown-artifact-public", "content", "Direct Markdown artifact",
            artifact.AvailableAsMarkdown ? "pass" : artifact.Expected ? "fail" : artifact.Fetched ? "warn" : "info",
            artifact.AvailableAsMarkdown
                ? $"Direct Markdown artifact is available at {artifact.Url}."
                : artifact.Fetched
                    ? $"Direct Markdown artifact was fetched at {artifact.Url}, but returned {FormatContentTypeForMessage(artifact.ContentType)}. Configure the host MIME type as text/markdown."
                    : artifact.Expected
                        ? $"The enabled Markdown artifact was not found at {artifact.Url}."
                        : $"Direct Markdown artifact was not found at {artifact.Url}.",
            artifact.Url);
    }

    private sealed record RemoteMarkdownArtifactScan(
        bool Expected,
        string Url,
        HttpTextResult? Result,
        string ContentType,
        bool Fetched,
        bool AvailableAsMarkdown);
}
