using System.Net.Http;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private static void AddRemoteSecurityHeaderChecks(
        List<WebAgentReadinessCheck> checks,
        HttpResponseMessage? response,
        string target,
        AgentReadinessSpec? readiness)
    {
        var security = readiness?.SecurityHeaders ?? new AgentSecurityHeadersSpec();
        var enabled = readiness?.Enabled != false && security.Enabled;
        // An unconfigured scanner retains its generic security baseline. A loaded
        // site policy can explicitly defer persistent HSTS while still requiring
        // the rest of the response-header baseline.
        AddRemoteHeaderCheck(checks, "security-hsts", "HSTS", response, "Strict-Transport-Security", target, readiness is null || enabled && security.Hsts);
        AddRemoteHeaderCheck(checks, "security-csp", "CSP", response, "Content-Security-Policy", target, enabled && security.ContentSecurityPolicy);
        AddRemoteHeaderCheck(checks, "security-xcto", "X-Content-Type-Options", response, "X-Content-Type-Options", target, enabled && security.XContentTypeOptions);

        var expectFrameProtection = enabled && security.XFrameOptions;
        var hasFrameProtection = HeaderExists(response, "X-Frame-Options") || HeaderContains(response, "Content-Security-Policy", "frame-ancestors");
        AddCheck(checks, "security-xfo", "security-trust", "X-Frame-Options",
            !expectFrameProtection ? "info" : hasFrameProtection ? "pass" : "fail",
            !expectFrameProtection
                ? "Clickjacking protection header verification is disabled by site policy."
                : hasFrameProtection
                    ? "Homepage response includes clickjacking protection."
                    : "Homepage response does not include X-Frame-Options or CSP frame-ancestors.",
            target);

        AddRemoteHeaderCheck(checks, "security-referrer-policy", "Referrer-Policy", response, "Referrer-Policy", target, enabled && security.ReferrerPolicy);
        AddRemoteHeaderCheck(checks, "security-permissions-policy", "Permissions-Policy", response, "Permissions-Policy", target, enabled && security.PermissionsPolicy);
    }

    private static void AddRemoteHeaderCheck(
        List<WebAgentReadinessCheck> checks,
        string id,
        string name,
        HttpResponseMessage? response,
        string headerName,
        string target,
        bool expected)
    {
        var present = HeaderExists(response, headerName);
        AddCheck(checks, id, "security-trust", name,
            !expected ? "info" : present ? "pass" : "fail",
            !expected
                ? $"{name} verification is disabled by site policy."
                : present
                    ? $"Homepage response includes {name}."
                    : $"Homepage response does not include {name}.",
            target);
    }

    private static void AddRemoteDiscoveryCorsCheck(
        List<WebAgentReadinessCheck> checks,
        AgentReadinessSpec readiness,
        IReadOnlyCollection<(bool Enabled, string Url, HttpResponseMessage? Response)> resources)
    {
        var security = readiness.SecurityHeaders ?? new AgentSecurityHeadersSpec();
        var expected = security.Enabled && security.CorsForWellKnown && !string.IsNullOrWhiteSpace(security.CorsAllowOrigin);
        var enabledResources = resources.Where(static resource => resource.Enabled).ToArray();
        if (!expected || enabledResources.Length == 0)
        {
            AddCheck(checks, "security-cors", "security-trust", "CORS", "info",
                !expected
                    ? "Discovery-resource CORS verification is disabled by site policy."
                    : "No enabled discovery resources require CORS verification.",
                enabledResources.FirstOrDefault().Url);
            return;
        }

        var expectedOrigin = security.CorsAllowOrigin!.Trim();
        var missing = enabledResources
            .Where(resource => !HeaderHasExactValue(resource.Response, "Access-Control-Allow-Origin", expectedOrigin))
            .Select(static resource => resource.Url)
            .ToArray();
        AddCheck(checks, "security-cors", "security-trust", "CORS",
            missing.Length == 0 ? "pass" : "fail",
            missing.Length == 0
                ? $"Every enabled discovery resource allows origin '{expectedOrigin}'."
                : $"Discovery-resource CORS is missing or differs from '{expectedOrigin}' at: {string.Join(", ", missing)}.",
            missing.FirstOrDefault() ?? enabledResources[0].Url);
    }

    private static bool HeaderHasExactValue(HttpResponseMessage? response, string name, string expected)
    {
        if (response is null)
            return false;

        return (response.Headers.TryGetValues(name, out var values) ||
                response.Content.Headers.TryGetValues(name, out values)) &&
               values.Any(value => string.Equals(value.Trim(), expected, StringComparison.Ordinal));
    }
}
