namespace PowerForgeStudio.Orchestrator.Connections;

internal static class ConnectionEndpointSanitizer
{
    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Local provider";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint)) return "Invalid endpoint";
        if (endpoint.Scheme.Equals("named-pipe", StringComparison.OrdinalIgnoreCase))
            return $"named-pipe://{endpoint.Host}{endpoint.AbsolutePath}";
        if (endpoint.Scheme is not ("https" or "http")) return "Unsupported endpoint";
        if (endpoint.Scheme == "http" && !endpoint.IsLoopback) return "Insecure endpoint blocked";
        var builder = new UriBuilder(endpoint)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
