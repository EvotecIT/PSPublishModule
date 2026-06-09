using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void EnforceExplicitAuditCheckContract(JsonElement step, string stepLabel)
    {
        var requireExplicitChecks = GetBool(step, "requireExplicitChecks") ?? GetBool(step, "require-explicit-checks") ?? false;
        if (!requireExplicitChecks)
            return;

        var missing = new List<string>();
        RequireExplicitCheck(step, missing, "checkSeoMeta", "check-seo-meta");
        RequireExplicitCheck(step, missing, "checkNetworkHints");
        RequireExplicitCheck(step, missing, "checkRenderBlockingResources", "checkRenderBlocking");
        RequireExplicitCheck(step, missing, "checkHeadingOrder");
        RequireExplicitCheck(step, missing, "checkLinkPurposeConsistency", "checkLinkPurpose");
        RequireExplicitCheck(step, missing, "checkMediaEmbeds", "checkMedia");

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{stepLabel}: requireExplicitChecks is enabled; add explicit boolean values for: {string.Join(", ", missing)}.");
    }

    private static void RequireExplicitCheck(JsonElement step, List<string> missing, string canonicalName, params string[] aliases)
    {
        if (HasAnyProperty(step, BuildPropertyNames(canonicalName, aliases)))
            return;

        missing.Add(canonicalName);
    }

    private static string[] BuildPropertyNames(string primary, string[] aliases)
    {
        if (aliases is null || aliases.Length == 0)
            return new[] { primary };

        var names = new string[aliases.Length + 1];
        names[0] = primary;
        Array.Copy(aliases, 0, names, 1, aliases.Length);
        return names;
    }
}
