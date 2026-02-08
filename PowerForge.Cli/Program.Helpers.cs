using PowerForge;
using PowerForge.Cli;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    static void PrintHelp()
    {
        Console.WriteLine(@"PowerForge CLI
    Usage:
      powerforge build --name <ModuleName> --project-root <path> --version <X.Y.Z> [--csproj <path>] [--staging <path>] [--configuration Release] [--framework <tfm>]* [--author <name>] [--company <name>] [--description <text>] [--tag <tag>]* [--output json]
      powerforge build [--config <BuildSpec|Pipeline>.json] [--project-root <path>] [--output json]
      powerforge docs [--config <Pipeline.json>] [--project-root <path>] [--output json]
      powerforge pack [--config <Pipeline.json>] [--project-root <path>] [--out <path>] [--output json]
      powerforge template --script <Build-Module.ps1> [--out <path>] [--project-root <path>] [--powershell <path>] [--output json]
      powerforge dotnet publish [--config <DotNetPublish.json>] [--project-root <path>] [--plan] [--validate] [--output json] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>] [--skip-restore] [--skip-build]
      powerforge normalize <files...>   Normalize encodings and line endings [--output json]
      powerforge format <files...>      Format scripts via PSScriptAnalyzer (out-of-proc) [--output json]
      powerforge test [--project-root <path>] [--test-path <path>] [--format Detailed|Normal|Minimal] [--coverage] [--force]
                      [--skip-dependencies] [--skip-import] [--keep-xml] [--timeout <seconds>] [--output json]
      powerforge test --config <TestSpec.json>
      powerforge install --name <ModuleName> --version <X.Y.Z> --staging <path> [--strategy exact|autorevision] [--keep N] [--root path]*
      powerforge install --config <InstallSpec.json>
      powerforge pipeline [--config <Pipeline.json>] [--project-root <path>] [--output json]
      powerforge plan [--config <Pipeline.json>] [--project-root <path>] [--output json]
      powerforge find --name <Name>[,<Name>...] [--repo <Repo>] [--version <X.Y.Z>] [--prerelease]
      powerforge publish --path <Path> [--repo <Repo>] [--tool auto|psresourceget|powershellget] [--apikey <Key>] [--nupkg]
                       [--destination <Path>] [--skip-dependencies-check] [--skip-manifest-validate]
                       [--repo-uri <Uri>] [--repo-source-uri <Uri>] [--repo-publish-uri <Uri>] [--repo-priority <N>] [--repo-api-version auto|v2|v3]
                       [--repo-trusted|--repo-untrusted] [--repo-ensure|--no-repo-ensure] [--repo-unregister-after-use]
                       [--repo-credential-username <User>] [--repo-credential-secret <Secret>] [--repo-credential-secret-file <Path>]
      --verbose, -Verbose              Enable verbose diagnostics
      --diagnostics                    Include logs in JSON output
      --quiet, -q                      Suppress non-essential output
      --no-color                       Disable ANSI colors
      --view auto|standard|ansi        Console rendering mode (default: auto)
      --output json                    Emit machine-readable JSON output      

    Default config discovery (when --config is omitted):
      Searches for powerforge.json / powerforge.pipeline.json / .powerforge/pipeline.json
      in the current directory and parent directories.
      DotNet publish searches for powerforge.dotnetpublish.json / .powerforge/dotnetpublish.json.
    ");
    }

    static CliOptions ParseCliOptions(string[]? args, out string? error)
    {
        error = null;
        if (args is null) return new CliOptions(verbose: false, quiet: false, diagnostics: false, noColor: false, view: ConsoleView.Auto);

        bool verbose = false;
        bool quiet = false;
        bool diagnostics = false;
        bool noColor = false;
        ConsoleView view = ConsoleView.Auto;
        bool viewExplicit = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
                continue;
            }

            if (a.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) || a.Equals("--diag", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = true;
                continue;
            }

            if (a.Equals("--no-color", StringComparison.OrdinalIgnoreCase) || a.Equals("--nocolor", StringComparison.OrdinalIgnoreCase))
            {
                noColor = true;
                continue;
            }

            if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
            {
                viewExplicit = true;
                if (++i >= args.Length)
                {
                    error = "Missing value for --view. Expected: auto|standard|ansi.";
                    return new CliOptions(verbose, quiet, diagnostics, noColor, view);
                }

                if (!TryParseConsoleView(args[i], out view))
                {
                    error = $"Invalid value for --view: '{args[i]}'. Expected: auto|standard|ansi.";
                    return new CliOptions(verbose, quiet, diagnostics, noColor, view);
                }

                continue;
            }
        }

        if (!viewExplicit)
        {
            var envView = Environment.GetEnvironmentVariable("POWERFORGE_VIEW");
            if (!string.IsNullOrWhiteSpace(envView) && TryParseConsoleView(envView, out var parsed))
            {
                view = parsed;
            }
        }

        return new CliOptions(verbose, quiet, diagnostics, noColor, view);
    }

    static string[] StripGlobalArgs(string[] args)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();

        var list = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (IsGlobalArg(a)) continue;

            if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
            {
                i++; // skip value
                continue;
            }

            list.Add(a);
        }
        return list.ToArray();
    }

    static bool IsGlobalArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return false;

        return arg.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("-q", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--diag", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--no-color", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--nocolor", StringComparison.OrdinalIgnoreCase);
    }

    static ILogger CreateTextLogger(CliOptions cli)
    {
        ILogger baseLogger = cli.NoColor
            ? new ConsoleLogger { IsVerbose = cli.Verbose }
            : new SpectreConsoleLogger { IsVerbose = cli.Verbose };

        return cli.Quiet ? new QuietLogger(baseLogger) : baseLogger;
    }

    static (ILogger Logger, BufferingLogger? Buffer) CreateCommandLogger(bool outputJson, CliOptions cli, ILogger textLogger)
    {
        if (!outputJson) return (textLogger, null);

        if (cli.Diagnostics)
        {
            var buffer = new BufferingLogger { IsVerbose = cli.Verbose };
            return (buffer, buffer);
        }

        return (new NullLogger { IsVerbose = cli.Verbose }, null);
    }

    static void WritePipelineSummary(ModulePipelineResult res, CliOptions cli, ILogger logger)
    {
        if (cli.Quiet) return;

        static int CountFileConsistencyIssues(ProjectConsistencyReport report, FileConsistencySettings? settings)
        {
            if (report is null) return 0;
            if (settings is null) return report.ProblematicFiles.Length;

            int count = 0;
            foreach (var f in report.ProblematicFiles)
            {
                if (f.NeedsEncodingConversion || f.NeedsLineEndingConversion)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMissingFinalNewline && f.MissingFinalNewline)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMixedLineEndings && f.HasMixedLineEndings)
                {
                    count++;
                }
            }

            return count;
        }

        static List<string> BuildFileConsistencyReasons(ProjectConsistencyFileDetail file, FileConsistencySettings? settings)
        {
            var reasons = new List<string>(4);

            if (file.NeedsEncodingConversion)
            {
                var current = file.CurrentEncoding?.ToString() ?? "Unknown";
                reasons.Add($"encoding {current} (expected {file.RecommendedEncoding})");
            }

            if (file.NeedsLineEndingConversion)
            {
                var current = file.CurrentLineEnding.ToString();
                reasons.Add($"line endings {current} (expected {file.RecommendedLineEnding})");
            }

            if (settings?.CheckMixedLineEndings == true && file.HasMixedLineEndings)
                reasons.Add("mixed line endings");

            if (settings?.CheckMissingFinalNewline == true && file.MissingFinalNewline)
                reasons.Add("missing final newline");

            var error = file.Error;
            if (!string.IsNullOrWhiteSpace(error))
                reasons.Add($"error: {error!.Trim()}");

            return reasons;
        }

        static void WriteFileConsistencyIssuesPlain(
            ProjectConsistencyReport report,
            FileConsistencySettings? settings,
            string label,
            CheckStatus status,
            ILogger logger)
        {
            if (report is null) return;
            var issues = report.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
            if (issues.Length == 0) return;

            var log = status == CheckStatus.Fail ? (Action<string>)logger.Error : logger.Warn;
            log($"File consistency issues ({label}): {issues.Length} file(s).");
            if (!string.IsNullOrWhiteSpace(report.ExportPath))
                log($"Report ({label}): {report.ExportPath}");

            const int maxItems = 20;
            var shown = 0;
            foreach (var item in issues)
            {
                var reasons = BuildFileConsistencyReasons(item, settings);
                if (reasons.Count == 0) continue;
                log($"{item.RelativePath} - {string.Join(", ", reasons)}");
                if (++shown >= maxItems) break;
            }

            if (issues.Length > maxItems)
                logger.Warn($"File consistency issues: {issues.Length - maxItems} more not shown.");
        }

        static void WriteFileConsistencyIssuesSpectre(
            ProjectConsistencyReport report,
            FileConsistencySettings? settings,
            string label,
            TableBorder border)
        {
            if (report is null) return;
            var issues = report.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
            if (issues.Length == 0) return;

            static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[grey]File consistency issues ({Esc(label)})[/]").LeftJustified());
            if (!string.IsNullOrWhiteSpace(report.ExportPath))
                AnsiConsole.MarkupLine($"[grey]Report:[/] {Esc(report.ExportPath)}");

            var table = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Path"))
                .AddColumn(new TableColumn("Issues"));

            const int maxItems = 20;
            var shown = 0;
            foreach (var item in issues)
            {
                var reasons = BuildFileConsistencyReasons(item, settings);
                if (reasons.Count == 0) continue;
                table.AddRow(Esc(item.RelativePath), Esc(string.Join(", ", reasons)));
                if (++shown >= maxItems) break;
            }

            AnsiConsole.Write(table);

            if (issues.Length > maxItems)
                AnsiConsole.MarkupLine($"[grey]... {issues.Length - maxItems} more not shown.[/]");
        }

        if (cli.NoColor)
        {
            logger.Success($"Pipeline built {res.Plan.ModuleName} {res.Plan.ResolvedVersion}");
            logger.Info($"Staging: {res.BuildResult.StagingPath}");

            var fileConsistencyConfig = res.Plan.FileConsistencySettings;
            if (fileConsistencyConfig?.Enable == true)
            {
                var scope = fileConsistencyConfig.ResolveScope();

                if (scope != FileConsistencyScope.ProjectOnly)
                {
                    if (res.FileConsistencyReport is not null)
                    {
                        var total = res.FileConsistencyReport.Summary.TotalFiles;
                        var issues = CountFileConsistencyIssues(res.FileConsistencyReport, fileConsistencyConfig);
                        var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                        logger.Info($"File consistency: {res.FileConsistencyStatus} ({compliance:0.0}% compliant)");
                        WriteFileConsistencyIssuesPlain(
                            res.FileConsistencyReport,
                            fileConsistencyConfig,
                            "staging",
                            res.FileConsistencyStatus ?? CheckStatus.Warning,
                            logger);
                    }
                    else
                    {
                        logger.Info("File consistency: disabled");
                    }
                }

                if (scope != FileConsistencyScope.StagingOnly)
                {
                    if (res.ProjectRootFileConsistencyReport is not null)
                    {
                        var total = res.ProjectRootFileConsistencyReport.Summary.TotalFiles;
                        var issues = CountFileConsistencyIssues(res.ProjectRootFileConsistencyReport, fileConsistencyConfig);
                        var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                        logger.Info($"File consistency (project): {res.ProjectRootFileConsistencyStatus} ({compliance:0.0}% compliant)");
                        WriteFileConsistencyIssuesPlain(
                            res.ProjectRootFileConsistencyReport,
                            fileConsistencyConfig,
                            "project",
                            res.ProjectRootFileConsistencyStatus ?? CheckStatus.Warning,
                            logger);
                    }
                    else
                    {
                        logger.Info("File consistency (project): disabled");
                    }
                }
            }
            else
            {
                logger.Info("File consistency: disabled");
            }

            if (res.CompatibilityReport is not null)
                logger.Info($"Compatibility: {res.CompatibilityReport.Summary.Status} ({res.CompatibilityReport.Summary.CrossCompatibilityPercentage:0.0}% cross-compatible)");
            else
                logger.Info("Compatibility: disabled");

            if (res.ValidationReport is not null)
                logger.Info($"Module validation: {res.ValidationReport.Status} ({res.ValidationReport.Summary})");
            else
                logger.Info("Module validation: disabled");

            if (res.Plan.Formatting is not null)
            {
                var staging = FormattingSummary.FromResults(res.FormattingStagingResults);
                var status = staging.Status;
                var parts = new List<string>(2) { FormattingSummary.FormatPartPlain("staging", staging) };

                if (res.Plan.Formatting.Options.UpdateProjectRoot)
                {
                    var project = FormattingSummary.FromResults(res.FormattingProjectResults);
                    status = FormattingSummary.Worst(status, project.Status);
                    parts.Add(FormattingSummary.FormatPartPlain("project", project));
                }

                logger.Info($"Formatting: {status} ({string.Join(", ", parts)})");
            }
            else
            {
                logger.Info("Formatting: disabled");
            }

            if (res.Plan.SignModule)
            {
                if (res.SigningResult is null)
                {
                    logger.Info("Signing: enabled");
                }
                else
                {
                    var s = res.SigningResult;
                    logger.Info($"Signing: {(s.Success ? "Pass" : "Fail")} (signed {s.SignedTotal} [new {s.SignedNew}, re {s.Resigned}], already {s.AlreadySignedOther} 3p/{s.AlreadySignedByThisCert} ours, failed {s.Failed})");
                }
            }
            else
            {
                logger.Info("Signing: disabled");
            }

            if (res.ArtefactResults is { Length: > 0 })
            {
                logger.Info($"Artefacts: {res.ArtefactResults.Length}");
                foreach (var a in res.ArtefactResults)
                    logger.Info($" - {a.Type}{(string.IsNullOrWhiteSpace(a.Id) ? string.Empty : $" ({a.Id})")}: {a.OutputPath}");
            }

            if (res.InstallResult is not null)
            {
                logger.Success($"Installed {res.Plan.ModuleName} {res.InstallResult.Version}");
                foreach (var path in res.InstallResult.InstalledPaths) logger.Info($" -> {path}");
            }

            return;
        }

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);
        static string StatusMarkup(CheckStatus status)
            => status switch
            {
                CheckStatus.Pass => "[green]Pass[/]",
                CheckStatus.Warning => "[yellow]Warning[/]",
                _ => "[red]Fail[/]"
            };

        static string SigningStatusMarkup(bool ok) => ok ? "[green]Pass[/]" : "[red]Fail[/]";

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;

        AnsiConsole.Write(new Rule($"[green]{(unicode ? "âœ…" : "OK")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "ðŸ“¦" : "*")} Module", $"{Esc(res.Plan.ModuleName)} [grey]{Esc(res.Plan.ResolvedVersion)}[/]");
        table.AddRow($"{(unicode ? "ðŸ§ª" : "*")} Staging", Esc(res.BuildResult.StagingPath));

        var fileConsistencySettings = res.Plan.FileConsistencySettings;
        if (fileConsistencySettings?.Enable == true)
        {
            var scope = fileConsistencySettings.ResolveScope();

            if (scope != FileConsistencyScope.ProjectOnly)
            {
                if (res.FileConsistencyReport is not null)
                {
                    var status = res.FileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.FileConsistencyReport.Summary.TotalFiles;
                    var issues = CountFileConsistencyIssues(res.FileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "ðŸ”Ž" : "*")} File consistency",
                        $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "ðŸ”Ž" : "*")} File consistency", "[grey]Disabled[/]");
                }
            }

            if (scope != FileConsistencyScope.StagingOnly)
            {
                if (res.ProjectRootFileConsistencyReport is not null)
                {
                    var status = res.ProjectRootFileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.ProjectRootFileConsistencyReport.Summary.TotalFiles;
                    var issues = CountFileConsistencyIssues(res.ProjectRootFileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "ðŸ”Ž" : "*")} File consistency (project)",
                        $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "ðŸ”Ž" : "*")} File consistency (project)", "[grey]Disabled[/]");
                }
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "ðŸ”Ž" : "*")} File consistency", "[grey]Disabled[/]");
        }

        if (res.CompatibilityReport is not null)
        {
            var s = res.CompatibilityReport.Summary;
            table.AddRow(
                $"{(unicode ? "ðŸ”Ž" : "*")} Compatibility",
                $"{StatusMarkup(s.Status)} [grey]{s.CrossCompatibilityPercentage:0.0}% cross-compatible[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "ðŸ”Ž" : "*")} Compatibility", "[grey]Disabled[/]");
        }

        if (res.ValidationReport is not null)
        {
            table.AddRow(
                $"{(unicode ? "ðŸ”Ž" : "*")} Module validation",
                $"{StatusMarkup(res.ValidationReport.Status)} [grey]{Esc(res.ValidationReport.Summary)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "ðŸ”Ž" : "*")} Module validation", "[grey]Disabled[/]");
        }

        if (res.Plan.Formatting is not null)
        {
            var staging = FormattingSummary.FromResults(res.FormattingStagingResults);
            var status = staging.Status;
            var parts = new List<string>(2) { FormattingSummary.FormatPartMarkup("staging", staging) };

            if (res.Plan.Formatting.Options.UpdateProjectRoot)
            {
                var project = FormattingSummary.FromResults(res.FormattingProjectResults);
                status = FormattingSummary.Worst(status, project.Status);
                parts.Add(FormattingSummary.FormatPartMarkup("project", project));
            }

            table.AddRow(
                $"{(unicode ? "ðŸŽ¨" : "*")} Formatting",
                $"{StatusMarkup(status)} [grey]{string.Join(", ", parts)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "ðŸŽ¨" : "*")} Formatting", "[grey]Disabled[/]");
        }

        if (res.Plan.SignModule)
        {
            if (res.SigningResult is null)
            {
                table.AddRow($"{(unicode ? "ðŸ”" : "*")} Signing", "[yellow]Enabled[/]");
            }
            else
            {
                var s = res.SigningResult;
                var value =
                    $"{SigningStatusMarkup(s.Success)} [grey]signed [green]{s.SignedTotal}[/] (new [green]{s.SignedNew}[/], re [green]{s.Resigned}[/]), already [grey]{s.AlreadySignedOther}[/] 3p/[grey]{s.AlreadySignedByThisCert}[/] ours, failed [red]{s.Failed}[/][/]";
                table.AddRow($"{(unicode ? "ðŸ”" : "*")} Signing", value);
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "ðŸ”" : "*")} Signing", "[grey]Disabled[/]");
        }

        if (res.ArtefactResults is { Length: > 0 })
            table.AddRow($"{(unicode ? "ðŸ“¦" : "*")} Artefacts", $"[green]{res.ArtefactResults.Length}[/]");
        else
            table.AddRow($"{(unicode ? "ðŸ“¦" : "*")} Artefacts", "[grey]None[/]");

        if (res.InstallResult is not null)
            table.AddRow($"{(unicode ? "ðŸ“¥" : "*")} Install", $"[green]{Esc(res.InstallResult.Version)}[/]");
        else
            table.AddRow($"{(unicode ? "ðŸ“¥" : "*")} Install", "[grey]Disabled[/]");

        AnsiConsole.Write(table);

        if (res.ValidationReport is not null && res.ValidationReport.Checks.Length > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[grey]Validation details[/]").LeftJustified());

            var details = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Check").NoWrap())
                .AddColumn(new TableColumn("Result"));

            foreach (var check in res.ValidationReport.Checks)
            {
                if (check is null) continue;
                details.AddRow(
                    Esc(check.Name),
                    $"{StatusMarkup(check.Status)} [grey]{Esc(check.Summary)}[/]");
            }

            AnsiConsole.Write(details);
        }

        if (res.FileConsistencyReport is not null)
            WriteFileConsistencyIssuesSpectre(res.FileConsistencyReport, res.Plan.FileConsistencySettings, "staging", border);

        if (res.ProjectRootFileConsistencyReport is not null)
            WriteFileConsistencyIssuesSpectre(res.ProjectRootFileConsistencyReport, res.Plan.FileConsistencySettings, "project", border);

        if (res.ArtefactResults is { Length: > 0 })
        {
            var artefacts = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Id").NoWrap())
                .AddColumn(new TableColumn("Path"));

            foreach (var a in res.ArtefactResults)
                artefacts.AddRow(Esc(a.Type.ToString()), Esc(a.Id ?? string.Empty), Esc(a.OutputPath));

            AnsiConsole.Write(artefacts);
        }

        if (res.InstallResult is not null && res.InstallResult.InstalledPaths is { Count: > 0 })
        {
            AnsiConsole.MarkupLine($"[grey]{(unicode ? "ðŸ“" : "*")} Installed paths:[/]");
            foreach (var path in res.InstallResult.InstalledPaths)
                AnsiConsole.MarkupLine($"  [grey]{(unicode ? "â†’" : "->")}[/] {Esc(path)}");
        }
    }

    static void WriteLogTail(BufferingLogger buffer, ILogger logger, int maxEntries = 80)
    {
        if (buffer is null) return;
        if (buffer.Entries.Count == 0) return;

        maxEntries = Math.Max(1, maxEntries);
        var total = buffer.Entries.Count;
        var start = Math.Max(0, total - maxEntries);
        var shown = total - start;

        logger.Warn($"Last {shown}/{total} log lines:");
        for (int i = start; i < total; i++)
        {
            var e = buffer.Entries[i];
            var msg = e.Message ?? string.Empty;
            switch (e.Level)
            {
                case "success": logger.Success(msg); break;
                case "warn": logger.Warn(msg); break;
                case "error": logger.Error(msg); break;
                case "verbose": logger.Verbose(msg); break;
                default: logger.Info(msg); break;
            }
        }
    }

    static void WriteDotNetPublishFailureDetails(DotNetPublishResult result, ILogger logger)
    {
        if (result is null) return;
        if (result.Failure is null) return;

        var f = result.Failure;
        logger.Warn($"Failure step: {f.StepKind} ({f.StepKey})");
        if (!string.IsNullOrWhiteSpace(f.LogPath))
            logger.Warn($"Failure log: {f.LogPath}");

        if (!string.IsNullOrWhiteSpace(f.StdErrTail))
            logger.Warn($"stderr (tail):{Environment.NewLine}{f.StdErrTail.TrimEnd()}");
        else if (!string.IsNullOrWhiteSpace(f.StdOutTail))
            logger.Warn($"stdout (tail):{Environment.NewLine}{f.StdOutTail.TrimEnd()}");
    }

    static T RunWithStatus<T>(bool outputJson, CliOptions cli, string statusText, Func<T> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));

        if (outputJson || cli.Quiet || cli.NoColor)
            return action();

        var view = ResolveConsoleView(cli.View);
        if (view != ConsoleView.Standard)
            return action();

        if (ConsoleEnvironment.IsCI || !Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive)
            return action();

        T? result = default;
        Spectre.Console.AnsiConsole.Status().Start(statusText, _ => { result = action(); });
        return result!;
    }

    static ConsoleView ResolveConsoleView(ConsoleView requested)
    {
        if (requested != ConsoleView.Auto) return requested;

        var interactive = Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive
            && !ConsoleEnvironment.IsCI;

        return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
    }

    static bool TryParseConsoleView(string? value, out ConsoleView view)
    {
        view = ConsoleView.Auto;
        if (string.IsNullOrWhiteSpace(value)) return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                view = ConsoleView.Auto;
                return true;
            case "standard":
            case "interactive":
                view = ConsoleView.Standard;
                return true;
            case "ansi":
            case "plain":
                view = ConsoleView.Ansi;
                return true;
            default:
                return false;
        }
    }

    static (string Name, string Source, string? Staging, string? Csproj, string Version, string Configuration, string[] Frameworks, string? Author, string? CompanyName, string? Description, string[] Tags, string? IconUri, string? ProjectUri)? ParseBuildArgs(string[] argv)
    {
        string? name = null, source = null, staging = null, csproj = null, version = null;
        string configuration = "Release";
        var frameworks = new List<string>();
        string? author = null, companyName = null, description = null, iconUri = null, projectUri = null;
        var tags = new List<string>();

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--name": name = ++i < argv.Length ? argv[i] : null; break;
                case "--project-root":
                case "--source": source = ++i < argv.Length ? argv[i] : null; break;
                case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
                case "--csproj": csproj = ++i < argv.Length ? argv[i] : null; break;
                case "--version": version = ++i < argv.Length ? argv[i] : null; break;
                case "--configuration": configuration = ++i < argv.Length ? argv[i] : configuration; break;
                case "--config": i++; break; // handled before ParseBuildArgs
                case "--output": i++; break; // handled elsewhere
                case "--framework":
                    if (++i < argv.Length)
                    {
                        foreach (var f in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            frameworks.Add(f.Trim());
                    }
                    break;
                case "--author": author = ++i < argv.Length ? argv[i] : null; break;
                case "--company": companyName = ++i < argv.Length ? argv[i] : null; break;
                case "--description": description = ++i < argv.Length ? argv[i] : null; break;
                case "--tag": if (++i < argv.Length) tags.Add(argv[i]); break;
                case "--tags":
                    if (++i < argv.Length)
                    {
                        foreach (var t in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            tags.Add(t.Trim());
                    }
                    break;
                case "--icon-uri": iconUri = ++i < argv.Length ? argv[i] : null; break;
                case "--project-uri": projectUri = ++i < argv.Length ? argv[i] : null; break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(version))
            return null;

        var tfms = frameworks.Count > 0 ? frameworks.ToArray() : new[] { "net472", "net8.0" };
        return (name!, source!, staging, csproj, version!, configuration, tfms, author, companyName, description, tags.ToArray(), iconUri, projectUri);
    }

    static (string Name, string Version, string Staging, string[] Roots, PowerForge.InstallationStrategy Strategy, int Keep)? ParseInstallArgs(string[] argv)
    {
        string? name = null, version = null, staging = null; var roots = new List<string>();
        var strategy = PowerForge.InstallationStrategy.Exact; int keep = 3;
        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            switch (a.ToLowerInvariant())
            {
                case "--name": name = ++i < argv.Length ? argv[i] : null; break;
                case "--version": version = ++i < argv.Length ? argv[i] : null; break;
                case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
                case "--root": if (++i < argv.Length) roots.Add(argv[i]); break;
                case "--strategy":
                    var s = ++i < argv.Length ? argv[i] : null;
                    if (string.Equals(s, "autorevision", StringComparison.OrdinalIgnoreCase)) strategy = PowerForge.InstallationStrategy.AutoRevision;
                    else strategy = PowerForge.InstallationStrategy.Exact;
                    break;
                case "--keep":
                    if (++i < argv.Length && int.TryParse(argv[i], out var k)) keep = k;
                    break;
                case "--config": i++; break; // handled before ParseInstallArgs
                case "--output": i++; break; // handled elsewhere
            }
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(staging))
            return null;
        return (name!, version!, staging!, roots.ToArray(), strategy, keep);        
    }

    static ModuleTestSuiteSpec? ParseTestArgs(string[] argv)
    {
        var spec = new ModuleTestSuiteSpec { ProjectPath = Directory.GetCurrentDirectory() };

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--project-root":
                case "--project":
                case "--path":
                    if (++i >= argv.Length) return null;
                    spec.ProjectPath = argv[i];
                    break;
                case "--test-path":
                    if (++i >= argv.Length) return null;
                    spec.TestPath = argv[i];
                    break;
                case "--format":
                case "--output-format":
                    if (++i >= argv.Length) return null;
                    if (!Enum.TryParse<ModuleTestSuiteOutputFormat>(argv[i], ignoreCase: true, out var fmt))
                        return null;
                    spec.OutputFormat = fmt;
                    break;
                case "--additional-modules":
                case "--modules":
                    if (++i >= argv.Length) return null;
                    spec.AdditionalModules = SplitCsv(argv[i]);
                    break;
                case "--skip-modules":
                    if (++i >= argv.Length) return null;
                    spec.SkipModules = SplitCsv(argv[i]);
                    break;
                case "--coverage":
                case "--enable-code-coverage":
                    spec.EnableCodeCoverage = true;
                    break;
                case "--force":
                    spec.Force = true;
                    break;
                case "--skip-dependencies":
                    spec.SkipDependencies = true;
                    break;
                case "--skip-import":
                    spec.SkipImport = true;
                    break;
                case "--keep-xml":
                case "--keep-results-xml":
                    spec.KeepResultsXml = true;
                    break;
                case "--timeout":
                case "--timeout-seconds":
                    if (++i >= argv.Length) return null;
                    if (!int.TryParse(argv[i], out var t)) return null;
                    spec.TimeoutSeconds = t;
                    break;
                case "--prefer-powershell":
                case "--prefer-windows-powershell":
                    spec.PreferPwsh = false;
                    break;
                case "--prefer-pwsh":
                    spec.PreferPwsh = true;
                    break;
                case "--config":
                    i++;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
            }
        }

        return spec;
    }

    static string[] SplitCsv(string value)
        => (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

    static string[] ParseCsvOptionValues(string[] argv, params string[] optionNames)
    {
        if (argv is null || argv.Length == 0) return Array.Empty<string>();
        if (optionNames is null || optionNames.Length == 0) return Array.Empty<string>();

        var values = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (!optionNames.Any(o => a.Equals(o, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (++i < argv.Length)
                values.AddRange(SplitCsv(argv[i]));
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static DotNetPublishStyle ParseDotNetPublishStyle(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Style must not be empty.", nameof(value));

        if (Enum.TryParse(raw, ignoreCase: true, out DotNetPublishStyle style))
            return style;

        throw new ArgumentException($"Unknown style: {raw}. Expected one of: {string.Join(", ", Enum.GetNames(typeof(DotNetPublishStyle)))}", nameof(value));
    }

    static void ApplyDotNetPublishOverrides(
        DotNetPublishSpec spec,
        string[] overrideTargets,
        string[] overrideRids,
        string[] overrideFrameworks,
        string? overrideStyle)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var targets = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            .Where(t => t is not null)
            .ToArray();

        if (overrideTargets is { Length: > 0 })
        {
            var selected = new HashSet<string>(overrideTargets.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var missing = selected.Where(n => targets.All(t => !t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown target(s): {string.Join(", ", missing)}", nameof(overrideTargets));

            targets = targets.Where(t => selected.Contains(t.Name)).ToArray();
            if (targets.Length == 0)
                throw new ArgumentException("No targets selected.", nameof(overrideTargets));

            spec.Targets = targets;
        }

        if (overrideRids is { Length: > 0 })
        {
            spec.DotNet.Runtimes = overrideRids;
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Runtimes = overrideRids;
            }
        }

        if (overrideFrameworks is { Length: > 0 })
        {
            var frameworks = overrideFrameworks
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (frameworks.Length == 0)
                throw new ArgumentException("Frameworks must not be empty.", nameof(overrideFrameworks));

            var tfm = frameworks[0];
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Framework = tfm;
                t.Publish.Frameworks = frameworks;
            }
        }

        if (!string.IsNullOrWhiteSpace(overrideStyle))
        {
            var style = ParseDotNetPublishStyle(overrideStyle);
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Style = style;
            }
        }
    }

    static void ApplyDotNetPublishSkipFlags(DotNetPublishPlan plan, bool skipRestore, bool skipBuild)
    {
        if (plan is null) return;

        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        if (skipRestore)
        {
            plan.NoRestoreInPublish = true;
            steps = steps.Where(s => s.Kind != DotNetPublishStepKind.Restore).ToArray();
        }

        if (skipBuild)
        {
            plan.NoBuildInPublish = true;
            steps = steps.Where(s => s.Kind != DotNetPublishStepKind.Build).ToArray();
        }

        plan.Steps = steps;
    }

    static string[] ValidateDotNetPublishPlan(DotNetPublishPlan plan)
    {
        var errors = new List<string>();
        if (plan is null)
        {
            errors.Add("Plan is null.");
            return errors.ToArray();
        }

        if (string.IsNullOrWhiteSpace(plan.ProjectRoot) || !Directory.Exists(plan.ProjectRoot))
            errors.Add($"ProjectRoot not found: {plan.ProjectRoot}");

        if (!string.IsNullOrWhiteSpace(plan.SolutionPath) && !File.Exists(plan.SolutionPath))
            errors.Add($"SolutionPath not found: {plan.SolutionPath}");

        if (plan.Targets is null || plan.Targets.Length == 0)
        {
            errors.Add("Targets must not be empty.");
        }
        else
        {
            foreach (var t in plan.Targets)
            {
                if (t is null) continue;
                if (string.IsNullOrWhiteSpace(t.Name))
                    errors.Add("Target.Name must not be empty.");
                if (string.IsNullOrWhiteSpace(t.ProjectPath) || !File.Exists(t.ProjectPath))
                    errors.Add($"ProjectPath not found for target '{t.Name}': {t.ProjectPath}");

                if (t.Publish is null)
                {
                    errors.Add($"Target.Publish is required for target '{t.Name}'.");
                    continue;
                }

                var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToArray();
                if (frameworks.Length == 0 && string.IsNullOrWhiteSpace(t.Publish.Framework))
                    errors.Add($"Target.Publish.Framework is required for target '{t.Name}' (or set Target.Publish.Frameworks).");

                if (t.Publish.Runtimes is null || t.Publish.Runtimes.Length == 0 || t.Publish.Runtimes.All(string.IsNullOrWhiteSpace))
                    errors.Add($"Target.Publish.Runtimes is empty for target '{t.Name}'.");
            }
        }

        if (plan.Steps is null || plan.Steps.Length == 0)
        {
            errors.Add("No steps planned.");
        }
        else
        {
            if (!plan.Steps.Any(s => s.Kind == DotNetPublishStepKind.Publish))
                errors.Add("No publish steps planned.");

            var dupKeys = plan.Steps
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (dupKeys.Length > 0)
                errors.Add($"Duplicate step keys: {string.Join(", ", dupKeys)}");
        }

        return errors.ToArray();
    }

    static string? TryGetOptionValue(string[] argv, string optionName)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            return ++i < argv.Length ? argv[i] : null;
        }
        return null;
    }

    static string? TryGetProjectRoot(string[] argv)
        => TryGetOptionValue(argv, "--project-root")
           ?? TryGetOptionValue(argv, "--project")
           ?? TryGetOptionValue(argv, "--path");

    static string ResolvePathFromBase(string baseDir, string path)
    {
        var raw = (path ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Path is empty.", nameof(path));

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
    }

    static string? ResolvePathFromBaseNullable(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var raw = path.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
    }

    static void ResolveBuildSpecPaths(ModuleBuildSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.SourcePath))
            spec.SourcePath = ResolvePathFromBase(baseDir, spec.SourcePath);
        spec.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.StagingPath);
        spec.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.CsprojPath);
    }

    static void ResolveInstallSpecPaths(ModuleInstallSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.StagingPath))
            spec.StagingPath = ResolvePathFromBase(baseDir, spec.StagingPath);
    }

    static void ResolveTestSpecPaths(ModuleTestSuiteSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.ProjectPath))
            spec.ProjectPath = ResolvePathFromBase(baseDir, spec.ProjectPath);
        spec.TestPath = ResolvePathFromBaseNullable(baseDir, spec.TestPath);
    }

    static void ResolvePipelineSpecPaths(ModulePipelineSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (spec.Build is null) return;

        if (!string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            spec.Build.SourcePath = ResolvePathFromBase(baseDir, spec.Build.SourcePath);
        spec.Build.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.Build.StagingPath);
        spec.Build.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.Build.CsprojPath);
    }

    static string? FindDefaultPipelineConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.json",
            "powerforge.pipeline.json",
            Path.Combine(".powerforge", "powerforge.json"),
            Path.Combine(".powerforge", "pipeline.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    static string? FindDefaultBuildConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.build.json",
            Path.Combine(".powerforge", "build.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return FindDefaultPipelineConfig(baseDir);
    }

    static string? FindDefaultDotNetPublishConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.dotnetpublish.json",
            Path.Combine(".powerforge", "dotnetpublish.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    static IEnumerable<string> EnumerateSelfAndParents(string? baseDir)       
    {
        string current;
        try
        {
            current = Path.GetFullPath(string.IsNullOrWhiteSpace(baseDir)
                ? Directory.GetCurrentDirectory()
                : baseDir.Trim().Trim('"'));
        }
        catch
        {
            current = Directory.GetCurrentDirectory();
        }

        while (true)
        {
            yield return current;

            try
            {
                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent)) yield break;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) yield break;
                current = parent;
            }
            catch
            {
                yield break;
            }
        }
    }

    static bool LooksLikePipelineSpec(string fullConfigPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fullConfigPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;
            return root.TryGetProperty("Build", out _) || root.TryGetProperty("Segments", out _) || root.TryGetProperty("Install", out _);
        }
        catch
        {
            return false;
        }
    }

    static string ResolveExistingFilePath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");
        return full;
    }

    static (ModulePipelineSpec Value, string FullPath) LoadPipelineSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModulePipelineSpec, full);
        return (spec, full);
    }

    static (ModuleBuildSpec Value, string FullPath) LoadBuildSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleBuildSpec, full);
        return (spec, full);
    }

    static (DotNetPublishSpec Value, string FullPath) LoadDotNetPublishSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.DotNetPublishSpec, full);
        return (spec, full);
    }

    static (ModuleInstallSpec Value, string FullPath) LoadInstallSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleInstallSpec, full);
        return (spec, full);
    }

    static (ModuleTestSuiteSpec Value, string FullPath) LoadTestSuiteSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleTestSuiteSpec, full);
        return (spec, full);
    }

    static (string[] Names, string? Version, bool Prerelease, string[] Repositories)? ParseFindArgs(string[] argv)
    {
        var names = new List<string>();
        var repos = new List<string>();
        string? version = null;
        bool prerelease = false;

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--name":
                    if (++i < argv.Length)
                    {
                        foreach (var n in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            names.Add(n.Trim());
                    }
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                case "--repo":
                case "--repository":
                    if (++i < argv.Length) repos.Add(argv[i]);
                    break;
                case "--version":
                    version = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--prerelease":
                    prerelease = true;
                    break;
            }
        }

        if (names.Count == 0) return null;
        return (names.ToArray(), version, prerelease, repos.ToArray());
    }

    static RepositoryPublishRequest? ParsePublishArgs(string[] argv)
    {
        string? path = null;
        string? repositoryName = null;
        string? apiKey = null;
        string? destination = null;
        bool isNupkg = false;
        bool skipDeps = false;
        bool skipManifest = false;

        PublishTool tool = PublishTool.Auto;

        string? repoUri = null;
        string? repoSourceUri = null;
        string? repoPublishUri = null;
        bool repoTrusted = true;
        bool repoTrustedProvided = false;
        int? repoPriority = null;
        RepositoryApiVersion repoApiVersion = RepositoryApiVersion.Auto;
        bool repoApiVersionProvided = false;
        bool ensureRepoRegistered = true;
        bool ensureRepoProvided = false;
        bool unregisterAfterUse = false;

        string? repoCredUser = null;
        string? repoCredSecret = null;
        string? repoCredSecretFile = null;

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--path":
                    path = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                case "--repo":
                case "--repository":
                    repositoryName = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--tool":
                    tool = ParsePublishTool(++i < argv.Length ? argv[i] : null);
                    break;
                case "--apikey":
                case "--api-key":
                    apiKey = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--destination":
                case "--destination-path":
                    destination = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--nupkg":
                    isNupkg = true;
                    break;
                case "--skip-dependencies-check":
                    skipDeps = true;
                    break;
                case "--skip-manifest-validate":
                case "--skip-module-manifest-validate":
                    skipManifest = true;
                    break;

                case "--repo-uri":
                    repoUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-source-uri":
                    repoSourceUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-publish-uri":
                    repoPublishUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-trusted":
                    repoTrusted = true;
                    repoTrustedProvided = true;
                    break;
                case "--repo-untrusted":
                    repoTrusted = false;
                    repoTrustedProvided = true;
                    break;
                case "--repo-priority":
                    if (++i < argv.Length && int.TryParse(argv[i], out var p)) repoPriority = p;
                    break;
                case "--repo-api-version":
                    repoApiVersion = ParseRepositoryApiVersion(++i < argv.Length ? argv[i] : null);
                    repoApiVersionProvided = true;
                    break;
                case "--repo-ensure":
                    ensureRepoRegistered = true;
                    ensureRepoProvided = true;
                    break;
                case "--no-repo-ensure":
                    ensureRepoRegistered = false;
                    ensureRepoProvided = true;
                    break;
                case "--repo-unregister-after-use":
                    unregisterAfterUse = true;
                    break;

                case "--repo-credential-username":
                    repoCredUser = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-credential-secret":
                    repoCredSecret = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-credential-secret-file":
                    repoCredSecretFile = ++i < argv.Length ? argv[i] : null;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(path)) return null;

        string? repositorySecret = null;
        if (!string.IsNullOrWhiteSpace(repoCredSecretFile))
        {
            var full = Path.GetFullPath(repoCredSecretFile!.Trim().Trim('"'));
            repositorySecret = File.ReadAllText(full).Trim();
        }
        else if (!string.IsNullOrWhiteSpace(repoCredSecret))
        {
            repositorySecret = repoCredSecret.Trim();
        }

        var anyRepositoryUriProvided =
            !string.IsNullOrWhiteSpace(repoUri) ||
            !string.IsNullOrWhiteSpace(repoSourceUri) ||
            !string.IsNullOrWhiteSpace(repoPublishUri);

        if (anyRepositoryUriProvided)
        {
            if (string.IsNullOrWhiteSpace(repositoryName))
                throw new InvalidOperationException("Repository name is required when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
            if (string.Equals(repositoryName.Trim(), "PSGallery", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Repository name cannot be 'PSGallery' when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
        }

        PublishRepositoryConfiguration? repoConfig = null;
        var hasRepoCred = !string.IsNullOrWhiteSpace(repoCredUser) && !string.IsNullOrWhiteSpace(repositorySecret);
        var hasRepoOptions =
            anyRepositoryUriProvided ||
            hasRepoCred ||
            repoPriority.HasValue ||
            repoApiVersionProvided ||
            repoTrustedProvided ||
            ensureRepoProvided ||
            unregisterAfterUse;

        if (hasRepoOptions)
        {
            repoConfig = new PublishRepositoryConfiguration
            {
                Name = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
                Uri = string.IsNullOrWhiteSpace(repoUri) ? null : repoUri.Trim(),
                SourceUri = string.IsNullOrWhiteSpace(repoSourceUri) ? null : repoSourceUri.Trim(),
                PublishUri = string.IsNullOrWhiteSpace(repoPublishUri) ? null : repoPublishUri.Trim(),
                Trusted = repoTrusted,
                Priority = repoPriority,
                ApiVersion = repoApiVersion,
                EnsureRegistered = ensureRepoRegistered,
                UnregisterAfterUse = unregisterAfterUse,
                Credential = hasRepoCred
                    ? new RepositoryCredential { UserName = repoCredUser!.Trim(), Secret = repositorySecret }
                    : null
            };
        }

        return new RepositoryPublishRequest
        {
            Path = path!,
            IsNupkg = isNupkg,
            RepositoryName = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
            Tool = tool,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            Repository = repoConfig,
            DestinationPath = string.IsNullOrWhiteSpace(destination) ? null : destination.Trim(),
            SkipDependenciesCheck = skipDeps,
            SkipModuleManifestValidate = skipManifest
        };
    }

    static PublishTool ParsePublishTool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PublishTool.Auto;

        var v = value.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return PublishTool.Auto;
        if (v.Equals("psresourceget", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psresource", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psrg", StringComparison.OrdinalIgnoreCase))
            return PublishTool.PSResourceGet;
        if (v.Equals("powershellget", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psget", StringComparison.OrdinalIgnoreCase))
            return PublishTool.PowerShellGet;

        throw new InvalidOperationException($"Invalid value for --tool: '{value}'. Expected: auto|psresourceget|powershellget.");
    }

    static RepositoryApiVersion ParseRepositoryApiVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return RepositoryApiVersion.Auto;

        var v = value.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return RepositoryApiVersion.Auto;
        if (v.Equals("v2", StringComparison.OrdinalIgnoreCase) || v.Equals("2", StringComparison.OrdinalIgnoreCase))
            return RepositoryApiVersion.V2;
        if (v.Equals("v3", StringComparison.OrdinalIgnoreCase) || v.Equals("3", StringComparison.OrdinalIgnoreCase))
            return RepositoryApiVersion.V3;

        throw new InvalidOperationException($"Invalid value for --repo-api-version: '{value}'. Expected: auto|v2|v3.");
    }

    static bool IsJsonOutput(string[] argv)
    {
        foreach (var a in argv)
        {
            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var output = TryGetOptionValue(argv, "--output");
        return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
    }

    static string[] ParseTargets(string[] argv)
    {
        var list = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            if (a.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(a);
        }

        return list.ToArray();
    }

    static JsonElement? LogsToJsonElement(BufferingLogger? logBuffer)
    {
        if (logBuffer is null) return null;
        if (logBuffer.Entries.Count == 0) return null;
        return CliJson.SerializeToElement(logBuffer.Entries.ToArray(), CliJson.Context.LogEntryArray);
    }

    static ProcessResult RunProcess(string command, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            return new ProcessResult(1, string.Empty, "Failed to start PowerShell process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    static string[] BuildPowerShellArgs(string script)
    {
        var args = new List<string> { "-NoProfile", "-NonInteractive" };
        if (OperatingSystem.IsWindows())
        {
            args.Add("-ExecutionPolicy");
            args.Add("Bypass");
        }
        args.Add("-Command");
        args.Add(script);
        return args.ToArray();
    }

    static string QuotePowerShellLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    static string BuildTemplateScript(string scriptPath, string outPath, string projectRoot)
    {
        var escapedScript = QuotePowerShellLiteral(scriptPath);
        var escapedConfig = QuotePowerShellLiteral(outPath);
        var escapedRoot = QuotePowerShellLiteral(projectRoot);

        return string.Join("; ", new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {escapedRoot}",
            "try {",
            "  Import-Module PSPublishModule -Force -ErrorAction Stop",
            $"  $targetJson = {escapedConfig}",
            "  function Invoke-ModuleBuild {",
            "    param([Parameter(ValueFromRemainingArguments = $true)][object[]]$Args)",
            "    $cmd = Get-Command -Name Invoke-ModuleBuild -CommandType Cmdlet -Module PSPublishModule",
            "    & $cmd @Args -JsonOnly -JsonPath $targetJson -NoInteractive",
            "  }",
            "  Set-Alias -Name Build-Module -Value Invoke-ModuleBuild -Scope Local",
            $"  . {escapedScript}",
            "} catch {",
            "  Write-Error $_.Exception.Message",
            "  exit 1",
            "}"
        });
    }

    static void WriteJson(CliJsonEnvelope envelope, Action<Utf8JsonWriter>? writeAdditionalProperties = null)
    {
        CliJsonWriter.Write(envelope, writeAdditionalProperties);
    }

    sealed record ProcessResult(int ExitCode, string Output, string Error);

}

sealed class LogEntry
{
    public string Level { get; }
    public string Message { get; }

    public LogEntry(string level, string message)
    {
        Level = level;
        Message = message;
    }
}

sealed class BufferingLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public List<LogEntry> Entries { get; } = new();

    public void Info(string message) => Entries.Add(new LogEntry("info", message));
    public void Success(string message) => Entries.Add(new LogEntry("success", message));
    public void Warn(string message) => Entries.Add(new LogEntry("warn", message));
    public void Error(string message) => Entries.Add(new LogEntry("error", message));
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new LogEntry("verbose", message));
    }
}

sealed class CliOptions
{
    public bool Verbose { get; }
    public bool Quiet { get; }
    public bool Diagnostics { get; }
    public bool NoColor { get; }
    public ConsoleView View { get; }

    public CliOptions(bool verbose, bool quiet, bool diagnostics, bool noColor, ConsoleView view)
    {
        Verbose = verbose;
        Quiet = quiet;
        Diagnostics = diagnostics;
        NoColor = noColor;
        View = view;
    }
}

sealed class QuietLogger : ILogger
{
    private readonly ILogger _inner;

    public QuietLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsVerbose => _inner.IsVerbose;

    public void Info(string message) { }
    public void Success(string message) { }
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
    public void Verbose(string message) { }
}

