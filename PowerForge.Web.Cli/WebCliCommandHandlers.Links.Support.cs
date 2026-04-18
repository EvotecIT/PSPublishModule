using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void WriteLinksReview404Summary(string? summaryPath, WebLink404ReviewResult result)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(result, WebCliJson.Context.WebLink404ReviewResult));
    }

    private static int CompleteLinksValidation(
        string command,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion,
        string? configPath,
        LinkValidationResult validation,
        bool success,
        string message,
        string? reportPath,
        string? duplicateReportPath)
    {
        var exitCode = success ? 0 : 1;
        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = success,
                ExitCode = exitCode,
                Error = success ? null : message,
                ConfigPath = configPath,
                Result = WebCliJson.SerializeToElement(validation, WebCliJson.Context.LinkValidationResult)
            });
            return exitCode;
        }

        if (success)
            logger.Success(message);
        else
            logger.Error(message);
        if (!string.IsNullOrWhiteSpace(reportPath))
            logger.Info($"Report: {reportPath}");
        if (!string.IsNullOrWhiteSpace(duplicateReportPath))
            logger.Info($"Duplicate report: {duplicateReportPath}");
        return exitCode;
    }

    private static WebLinksCommandConfig LoadLinksSpecForCommand(string[] args, string command, bool outputJson, WebConsoleLogger logger)
    {
        var configPath = TryGetOptionValue(args, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return new WebLinksCommandConfig
            {
                BaseDir = Directory.GetCurrentDirectory()
            };
        }

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (siteSpec, siteSpecPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        return new WebLinksCommandConfig
        {
            Spec = siteSpec.Links,
            ConfigPath = siteSpecPath,
            BaseDir = Path.GetDirectoryName(siteSpecPath) ?? Directory.GetCurrentDirectory(),
            HasConfig = true
        };
    }

    private static WebLinkLoadOptions BuildLinkLoadOptions(string[] args, LinkServiceSpec? links, string baseDir)
    {
        var redirectsPath = ResolvePathForLinks(baseDir,
            TryGetOptionValue(args, "--redirects") ??
            TryGetOptionValue(args, "--redirects-path") ??
            TryGetOptionValue(args, "--redirectsPath"),
            links?.Redirects);

        var shortlinksPath = ResolvePathForLinks(baseDir,
            TryGetOptionValue(args, "--shortlinks") ??
            TryGetOptionValue(args, "--shortlinks-path") ??
            TryGetOptionValue(args, "--shortlinksPath"),
            links?.Shortlinks);

        var csvSources = ReadOptionList(args,
            "--source",
            "--sources",
            "--redirect-csv",
            "--redirect-csv-path",
            "--redirect-csv-paths",
            "--csv-source",
            "--csv-sources");
        var csvPaths = csvSources.Count > 0
            ? csvSources.Select(value => ResolvePathRelative(baseDir, value))
            : (links?.RedirectCsvPaths ?? Array.Empty<string>()).Select(value => ResolvePathRelative(baseDir, value));

        return new WebLinkLoadOptions
        {
            RedirectsPath = redirectsPath,
            ShortlinksPath = shortlinksPath,
            RedirectCsvPaths = csvPaths
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Hosts = BuildLinkHostMap(args, links),
            LanguageRootHosts = BuildLinkLanguageRootHostMap(args, links)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildLinkHostMap(string[] args, LinkServiceSpec? links)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (links?.Hosts is not null)
        {
            foreach (var pair in links.Hosts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    hosts[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        AddMapEntries(hosts, ReadOptionList(args, "--host", "--hosts", "--host-map", "--hostMap"), trimValueSlashes: false);
        return hosts;
    }

    private static IReadOnlyDictionary<string, string> BuildLinkLanguageRootHostMap(string[] args, LinkServiceSpec? links)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (links?.LanguageRootHosts is not null)
        {
            foreach (var pair in links.LanguageRootHosts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    hosts[pair.Key.Trim()] = pair.Value.Trim().Trim('/');
            }
        }

        AddMapEntries(hosts, ReadOptionList(args, "--language-root-host", "--language-root-hosts", "--languageRootHost", "--languageRootHosts"), trimValueSlashes: true);
        return hosts;
    }

    private static void AddMapEntries(Dictionary<string, string> target, IEnumerable<string> entries, bool trimValueSlashes)
    {
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');
            if (separator < 0)
                separator = entry.IndexOf(':');
            if (separator <= 0 || separator >= entry.Length - 1)
                continue;

            var key = entry[..separator].Trim();
            var value = entry[(separator + 1)..].Trim();
            if (trimValueSlashes)
                value = value.Trim('/');
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                target[key] = value;
        }
    }

    private static bool HasDirectLinkSources(string[] args)
        => !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--redirects")) ||
           !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--redirects-path")) ||
           !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--redirectsPath")) ||
           !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--shortlinks")) ||
           !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--shortlinks-path")) ||
           !string.IsNullOrWhiteSpace(TryGetOptionValue(args, "--shortlinksPath")) ||
           ReadOptionList(args, "--source", "--sources", "--redirect-csv", "--redirect-csv-path", "--redirect-csv-paths", "--csv-source", "--csv-sources").Count > 0;

    private static string? ResolvePathForLinks(string baseDir, string? directValue, string? configValue)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
            return ResolvePathRelative(baseDir, directValue);
        return string.IsNullOrWhiteSpace(configValue) ? null : ResolvePathRelative(baseDir, configValue);
    }

    private static string? ResolveOptionalPath(string baseDir, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ResolvePathRelative(baseDir, value);

    private static void WriteLinks404Report(string reportPath, WebLink404ReportResult result)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, WebCliJson.Context.WebLink404ReportResult));
    }

    private sealed class WebLinksCommandConfig
    {
        public LinkServiceSpec? Spec { get; init; }
        public string? ConfigPath { get; init; }
        public string BaseDir { get; init; } = Directory.GetCurrentDirectory();
        public bool HasConfig { get; init; }
    }
}
