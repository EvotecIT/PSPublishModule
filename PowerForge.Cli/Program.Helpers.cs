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

        AnsiConsole.Write(new Rule($"[green]{(unicode ? "\u2705" : "OK")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "\U0001F4E6" : "*")} Module", $"{Esc(res.Plan.ModuleName)} [grey]{Esc(res.Plan.ResolvedVersion)}[/]");
        table.AddRow($"{(unicode ? "\U0001F9EA" : "*")} Staging", Esc(res.BuildResult.StagingPath));

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
                        $"{(unicode ? "\U0001F50E" : "*")} File consistency",
                        $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "\U0001F50E" : "*")} File consistency", "[grey]Disabled[/]");
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
                        $"{(unicode ? "\U0001F50E" : "*")} File consistency (project)",
                        $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "\U0001F50E" : "*")} File consistency (project)", "[grey]Disabled[/]");
                }
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "\U0001F50E" : "*")} File consistency", "[grey]Disabled[/]");
        }

        if (res.CompatibilityReport is not null)
        {
            var s = res.CompatibilityReport.Summary;
            table.AddRow(
                $"{(unicode ? "\U0001F50E" : "*")} Compatibility",
                $"{StatusMarkup(s.Status)} [grey]{s.CrossCompatibilityPercentage:0.0}% cross-compatible[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "\U0001F50E" : "*")} Compatibility", "[grey]Disabled[/]");
        }

        if (res.ValidationReport is not null)
        {
            table.AddRow(
                $"{(unicode ? "\U0001F50E" : "*")} Module validation",
                $"{StatusMarkup(res.ValidationReport.Status)} [grey]{Esc(res.ValidationReport.Summary)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "\U0001F50E" : "*")} Module validation", "[grey]Disabled[/]");
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
                $"{(unicode ? "\U0001F3A8" : "*")} Formatting",
                $"{StatusMarkup(status)} [grey]{string.Join(", ", parts)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "\U0001F3A8" : "*")} Formatting", "[grey]Disabled[/]");
        }

        if (res.Plan.SignModule)
        {
            if (res.SigningResult is null)
            {
                table.AddRow($"{(unicode ? "\U0001F50F" : "*")} Signing", "[yellow]Enabled[/]");
            }
            else
            {
                var s = res.SigningResult;
                var value =
                    $"{SigningStatusMarkup(s.Success)} [grey]signed [green]{s.SignedTotal}[/] (new [green]{s.SignedNew}[/], re [green]{s.Resigned}[/]), already [grey]{s.AlreadySignedOther}[/] 3p/[grey]{s.AlreadySignedByThisCert}[/] ours, failed [red]{s.Failed}[/][/]";
                table.AddRow($"{(unicode ? "\U0001F50F" : "*")} Signing", value);
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "\U0001F50F" : "*")} Signing", "[grey]Disabled[/]");
        }

        if (res.ArtefactResults is { Length: > 0 })
            table.AddRow($"{(unicode ? "\U0001F4E6" : "*")} Artefacts", $"[green]{res.ArtefactResults.Length}[/]");
        else
            table.AddRow($"{(unicode ? "\U0001F4E6" : "*")} Artefacts", "[grey]None[/]");

        if (res.InstallResult is not null)
            table.AddRow($"{(unicode ? "\U0001F4E5" : "*")} Install", $"[green]{Esc(res.InstallResult.Version)}[/]");
        else
            table.AddRow($"{(unicode ? "\U0001F4E5" : "*")} Install", "[grey]Disabled[/]");

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
            AnsiConsole.MarkupLine($"[grey]{(unicode ? "\U0001F4CD" : "*")} Installed paths:[/]");
            foreach (var path in res.InstallResult.InstalledPaths)
                AnsiConsole.MarkupLine($"  [grey]{(unicode ? "\u2192" : "->")}[/] {Esc(path)}");
        }
    }
}
