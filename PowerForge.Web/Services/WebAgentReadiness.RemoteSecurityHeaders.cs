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
        AddCheck(checks, "security-cors", "security-trust", "CORS",
            HeaderExists(response, "Access-Control-Allow-Origin") ? "pass" : "warn",
            HeaderExists(response, "Access-Control-Allow-Origin") ? "Homepage response includes CORS." : "Homepage response does not include CORS; this is usually only required on API or discovery resources.",
            target);
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
}
