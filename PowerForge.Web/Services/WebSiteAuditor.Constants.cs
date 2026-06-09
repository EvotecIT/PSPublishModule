using System;

namespace PowerForge.Web;

public static partial class WebSiteAuditor
{
    private static readonly string[] DefaultExcludePatterns =
    {
        "*.scripts.html",
        "**/*.scripts.html",
        "*.head.html",
        "**/*.head.html",
        "**/api-fragments/**",
        "api-fragments/**"
    };
}

