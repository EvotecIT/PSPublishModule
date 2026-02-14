using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly string[] HostingTargetsAll = { "netlify", "azure", "vercel", "apache", "nginx", "iis" };

    private static void ExecuteHosting(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("hosting requires siteRoot.");

        var fullSiteRoot = Path.GetFullPath(siteRoot);
        if (!Directory.Exists(fullSiteRoot))
            throw new InvalidOperationException($"hosting siteRoot not found: {fullSiteRoot}");

        var removeUnselected = GetBool(step, "removeUnselected") ??
                               GetBool(step, "remove-unselected") ??
                               GetBool(step, "clean") ??
                               true;
        var strict = GetBool(step, "strict") ?? false;
        var dryRun = GetBool(step, "dryRun") ?? GetBool(step, "dry-run") ?? false;
        var selectedTargets = ResolveHostingTargets(step);

        var removed = 0;
        var kept = 0;
        var missingSelected = new List<string>();

        foreach (var target in HostingTargetsAll)
        {
            var fileName = ResolveHostingFileName(target);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var filePath = Path.Combine(fullSiteRoot, fileName);
            var selected = selectedTargets.Contains(target, StringComparer.OrdinalIgnoreCase);
            if (selected)
            {
                if (File.Exists(filePath))
                    kept++;
                else
                    missingSelected.Add(target);
                continue;
            }

            if (!removeUnselected || !File.Exists(filePath))
                continue;

            if (!dryRun)
                File.Delete(filePath);
            removed++;
        }

        if (strict && missingSelected.Count > 0)
            throw new InvalidOperationException($"hosting strict mode: missing selected artifacts: {string.Join(", ", missingSelected)}.");

        var selectedLabel = string.Join(",", selectedTargets.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        stepResult.Success = true;
        stepResult.Message = $"hosting ok: targets={selectedLabel}; kept={kept}; removed={removed}; missing={missingSelected.Count}";
    }

    private static string[] ResolveHostingTargets(JsonElement step)
    {
        var tokens = new List<string>();
        AddTokens(tokens, GetString(step, "target"));
        AddTokens(tokens, GetString(step, "targets"));
        AddTokens(tokens, GetString(step, "hosts"));
        AddTokens(tokens, GetString(step, "hostTargets") ?? GetString(step, "host-targets"));
        AddTokens(tokens, GetArrayOfStrings(step, "targets"));
        AddTokens(tokens, GetArrayOfStrings(step, "hosts"));
        AddTokens(tokens, GetArrayOfStrings(step, "hostTargets") ?? GetArrayOfStrings(step, "host-targets"));

        if (tokens.Count == 0)
            return HostingTargetsAll.ToArray();

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var value = token.Trim();
            if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var target in HostingTargetsAll)
                    normalized.Add(target);
                continue;
            }

            var mapped = NormalizeHostingTarget(value);
            if (string.IsNullOrWhiteSpace(mapped))
                throw new InvalidOperationException($"hosting has unsupported target '{value}'. Supported targets: {string.Join(", ", HostingTargetsAll)}.");

            normalized.Add(mapped);
        }

        return normalized.Count == 0
            ? HostingTargetsAll.ToArray()
            : normalized.ToArray();
    }

    private static void AddTokens(List<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        target.AddRange(CliPatternHelper.SplitPatterns(value));
    }

    private static void AddTokens(List<string> target, string[]? values)
    {
        if (values is null || values.Length == 0)
            return;

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value.Trim());
        }
    }

    private static string? NormalizeHostingTarget(string value)
    {
        var token = value.Trim().ToLowerInvariant();
        return token switch
        {
            "netlify" => "netlify",
            "azure" => "azure",
            "azure-swa" => "azure",
            "swa" => "azure",
            "staticwebapp" => "azure",
            "static-web-app" => "azure",
            "vercel" => "vercel",
            "apache" => "apache",
            "apache2" => "apache",
            "apache-2" => "apache",
            "htaccess" => "apache",
            ".htaccess" => "apache",
            "nginx" => "nginx",
            "nginx-conf" => "nginx",
            "nginx.conf" => "nginx",
            "nginxconfig" => "nginx",
            "iis" => "iis",
            "microsoft-iis" => "iis",
            "webconfig" => "iis",
            "web.config" => "iis",
            _ => null
        };
    }

    private static string ResolveHostingFileName(string target)
    {
        return target.ToLowerInvariant() switch
        {
            "netlify" => "_redirects",
            "azure" => "staticwebapp.config.json",
            "vercel" => "vercel.json",
            "apache" => ".htaccess",
            "nginx" => "nginx.redirects.conf",
            "iis" => "web.config",
            _ => string.Empty
        };
    }
}
