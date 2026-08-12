using System;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

/// <summary>Builds host-specific rule ownership keys and recognizes the previous host-scoped description format.</summary>
internal static class CloudflareManagedRuleOwnership
{
    internal static string BuildPrefix(string policyName, string hostname) =>
        $"PowerForge {policyName} [{hostname}]:";

    internal static bool IsLegacyRuleForHost(JsonObject rule, string policyName, string hostname)
    {
        var description = rule["description"]?.GetValue<string>() ?? string.Empty;
        var expression = rule["expression"]?.GetValue<string>() ?? string.Empty;
        return description.StartsWith($"PowerForge {policyName}:", StringComparison.Ordinal) &&
               expression.Contains($"http.host eq \"{hostname}\"", StringComparison.Ordinal);
    }
}
