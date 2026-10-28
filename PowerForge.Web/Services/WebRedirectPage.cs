namespace PowerForge.Web;

/// <summary>Shared HTML redirect rendering for static hosts and API compatibility pages.</summary>
internal static class WebRedirectPage
{
    internal static string Render(string canonical, string marker, bool preserveQueryAndFragment = false, bool preserveQuery = true)
    {
        var encoded = System.Web.HttpUtility.HtmlEncode(canonical);
        var script = System.Web.HttpUtility.JavaScriptStringEncode(canonical);
        var navigation = $"window.location.replace('{script}');";
        if (preserveQueryAndFragment)
        {
            navigation = $"var target = new URL('{script}', window.location.origin);\n" +
                (preserveQuery ? "    if (window.location.search) target.search += (target.search ? '&' : '?') + window.location.search.substring(1);\n" : string.Empty) +
                "    if (!target.hash) target.hash = window.location.hash;\n" +
                "    window.location.replace(target.href);";
        }
        return $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Redirecting...</title>
  <meta name=""robots"" content=""noindex,follow"" data-pf=""{marker}"" />
  <meta http-equiv=""refresh"" content=""0;url={encoded}"" />
  <link rel=""canonical"" href=""{encoded}"" />
</head>
<body>
  <p>This page moved to <a href=""{encoded}"">{encoded}</a>.</p>
  <script>
    {navigation}
  </script>
</body>
</html>
";
    }
}
