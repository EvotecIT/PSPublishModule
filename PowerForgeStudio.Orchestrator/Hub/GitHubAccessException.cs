using System.Net;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Safe display text without response bodies, credentials, or request URLs.</summary>
public sealed class GitHubAccessException(HttpStatusCode statusCode) : Exception(statusCode switch
{
    HttpStatusCode.Unauthorized => "GitHub authentication failed. Check the configured credential or gh login.",
    HttpStatusCode.Forbidden => "GitHub denied access or rate-limited this request. Check permissions and retry later.",
    HttpStatusCode.NotFound => "GitHub could not find this item, or the credential cannot access it.",
    HttpStatusCode.Gone => "This GitHub resource is no longer available.",
    HttpStatusCode.TooManyRequests => "GitHub rate-limited this request. Retry later.",
    _ => $"GitHub request failed (HTTP {(int)statusCode}). Retry later."
})
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
