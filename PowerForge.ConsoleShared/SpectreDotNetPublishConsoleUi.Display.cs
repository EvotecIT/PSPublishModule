using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static partial class SpectreDotNetPublishConsoleUi
{
    private static void WriteHeader(IAnsiConsole console, DotNetPublishPlan plan, string? configPath)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);
        static string Icon(string? s) => Esc(NormalizeIcon(s));

        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var titleTarget = !string.IsNullOrWhiteSpace(plan.SolutionPath)
            ? Path.GetFileName(plan.SolutionPath)
            : plan.ProjectRoot;

        var title = unicode
            ? $"📦 dotnet publish • {titleTarget}"
            : $"dotnet publish • {titleTarget}";

        console.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var iconColWidth = unicode ? 2 : 3;
        var info = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(BuildHeaderIconColumn(iconColWidth))
            .AddColumn(BuildHeaderKeyColumn())
            .AddColumn(BuildHeaderValueColumn());

        void AddInfoRow(string icon, string label, string valueMarkup)
            => info.AddRow($"[grey]{Icon(icon)}[/]", $"[grey]{Esc(label)}[/]", valueMarkup);

        var cfgText = string.IsNullOrWhiteSpace(configPath) ? "(discovered)" : configPath;
        AddInfoRow(unicode ? "⚙️" : "CFG", "Config", Esc(cfgText));
        AddInfoRow(unicode ? "📁" : "DIR", "Project", Esc(plan.ProjectRoot));
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
            AddInfoRow(unicode ? "🧩" : "SLN", "Solution", Esc(plan.SolutionPath));
        AddInfoRow(unicode ? "⚙️" : "CFG", "Configuration", Esc(plan.Configuration));

        var runtimes = plan.Targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AddInfoRow(unicode ? "🎯" : "TGT", "Targets", plan.Targets.Length.ToString());
        AddInfoRow(unicode ? "🖥️" : "RID", "Runtimes", runtimes.Length == 0 ? "(none)" : Esc(string.Join(", ", runtimes)));
        AddInfoRow(unicode ? "🧾" : "STP", "Steps", (plan.Steps?.Length ?? 0).ToString());

        console.Write(info);
        console.WriteLine();
    }

    private static string NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return string.Empty;
        return icon!.Replace("\uFE0F", string.Empty).Replace("\uFE0E", string.Empty);
    }

    private static TableColumn BuildHeaderIconColumn(int width)
    {
        var col = new TableColumn("i").NoWrap().Width(width);
        // Table uses per-column padding; remove left padding so the header aligns with progress output.
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderKeyColumn()
    {
        var col = new TableColumn("k").NoWrap();
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderValueColumn()
    {
        var col = new TableColumn("v");
        col.Padding = new Padding(0, 0, 0, 0);
        return col;
    }

    private static void WriteSummary(IAnsiConsole console, DotNetPublishPlan plan, DotNetPublishResult result)
    {
        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var title = unicode
            ? (result.Succeeded ? "✅ Summary" : "❌ Summary")
            : "Summary";

        console.Write(new Rule($"[grey]{Markup.Escape(title)}[/]") { Justification = Justify.Left });

        var totalBytes = result.Artefacts?.Sum(a => a.TotalBytes) ?? 0L;

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[grey]Item[/]").NoWrap())
            .AddColumn(new TableColumn("Value"));

        summary.AddRow("Succeeded", result.Succeeded ? "[green]true[/]" : "[red]false[/]");
        summary.AddRow("Artefacts", (result.Artefacts?.Length ?? 0).ToString());
        if (result.MsiPrepares is { Length: > 0 }) summary.AddRow("MSI prepares", result.MsiPrepares.Length.ToString());
        if (result.MsiBuilds is { Length: > 0 }) summary.AddRow("MSI builds", result.MsiBuilds.Length.ToString());
        if (result.StorePackages is { Length: > 0 }) summary.AddRow("Store packages", result.StorePackages.Length.ToString());
        if (result.BenchmarkGates is { Length: > 0 }) summary.AddRow("Benchmark gates", result.BenchmarkGates.Length.ToString());
        summary.AddRow("Bytes", totalBytes.ToString("N0"));
        if (!string.IsNullOrWhiteSpace(result.ManifestJsonPath))
            summary.AddRow("Manifest", Markup.Escape(result.ManifestJsonPath));
        if (!string.IsNullOrWhiteSpace(result.RunReportPath))
            summary.AddRow("Run report", Markup.Escape(result.RunReportPath));
        if (!string.IsNullOrWhiteSpace(result.RunReportMarkdownPath))
            summary.AddRow("Run report MD", Markup.Escape(result.RunReportMarkdownPath));

        if (!result.Succeeded && result.Failure is not null)
        {
            var step = $"{result.Failure.StepKind} ({result.Failure.StepKey})";
            summary.AddRow("Step", Markup.Escape(step));
            if (!string.IsNullOrWhiteSpace(result.Failure.LogPath))
                summary.AddRow("Log", Markup.Escape(result.Failure.LogPath));
        }

        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            var hasOutputTail = result.Failure is not null &&
                (!string.IsNullOrWhiteSpace(result.Failure.StdErrTail) || !string.IsNullOrWhiteSpace(result.Failure.StdOutTail));
            var message = hasOutputTail
                ? SpectrePipelineSummaryWriter.GetFailureHeadline(result.ErrorMessage)
                : result.ErrorMessage;
            summary.AddRow("Error", $"[red]{Markup.Escape(message)}[/]");
        }

        console.Write(summary);

        if (!result.Succeeded && result.Failure is not null)
        {
            var tail = !string.IsNullOrWhiteSpace(result.Failure.StdErrTail)
                ? result.Failure.StdErrTail
                : result.Failure.StdOutTail;

            if (tail is not null && !string.IsNullOrWhiteSpace(tail))
            {
                var header = unicode ? "📄 Output tail" : "Output tail";
                console.WriteLine();
                console.Write(new Rule($"[grey]{Markup.Escape(header)}[/]") { Justification = Justify.Left });

                var panel = new Panel(new Text(tail.TrimEnd()))
                {
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(1, 0, 1, 0)
                };
                panel.Header = new PanelHeader(string.IsNullOrWhiteSpace(result.Failure.StdErrTail) ? "stdout" : "stderr");

                console.Write(panel);
                console.WriteLine();
            }
        }

        if (result.Artefacts is null || result.Artefacts.Length == 0) return;

        if (console.Profile.Width < 120)
        {
            foreach (var artifact in result.Artefacts)
            {
                console.WriteLine();
                var details = new Table().Border(TableBorder.None).HideHeaders()
                    .AddColumn(new TableColumn("Item").NoWrap())
                    .AddColumn(new TableColumn("Value"));
                details.AddRow("Target", Markup.Escape(artifact.Target));
                details.AddRow("Framework", Markup.Escape(artifact.Framework));
                details.AddRow("Runtime", Markup.Escape(artifact.Runtime));
                details.AddRow("Style", Markup.Escape(artifact.Style.ToString()));
                details.AddRow("Output", Markup.Escape(artifact.OutputDir));
                if (!string.IsNullOrWhiteSpace(artifact.ZipPath))
                    details.AddRow("Zip", Markup.Escape(artifact.ZipPath));
                console.Write(details);
            }
            return;
        }

        var artefacts = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("Target").NoWrap())
            .AddColumn(new TableColumn("Framework").NoWrap())
            .AddColumn(new TableColumn("Runtime").NoWrap())
            .AddColumn(new TableColumn("Style").NoWrap())
            .AddColumn(new TableColumn("Output"))
            .AddColumn(new TableColumn("Zip"));

        foreach (var a in result.Artefacts)
        {
            artefacts.AddRow(
                Markup.Escape(a.Target),
                Markup.Escape(a.Framework),
                Markup.Escape(a.Runtime),
                Markup.Escape(a.Style.ToString()),
                Markup.Escape(a.OutputDir),
                string.IsNullOrWhiteSpace(a.ZipPath) ? string.Empty : Markup.Escape(a.ZipPath));
        }

        console.WriteLine();
        console.Write(artefacts);
        console.WriteLine();
    }

}
