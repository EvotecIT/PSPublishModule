using System.Collections.Generic;
using System.Linq;
using ConsoleMonster.Layout;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal static partial class ExecutablePublishConsoleUi
{
    private static void WriteSummary(DotNetPublishPlan plan, DotNetPublishResult result)
    {
        var facts = new KeyValueList();
        facts.Add("Succeeded", result.Succeeded ? "[pass]true[/]" : "[fail]false[/]");
        facts.Add("Artefacts", (result.Artefacts?.Length ?? 0).ToString());
        if (result.MsiPrepares is { Length: > 0 }) facts.Add("MSI prepares", result.MsiPrepares.Length.ToString());
        if (result.MsiBuilds is { Length: > 0 }) facts.Add("MSI builds", result.MsiBuilds.Length.ToString());
        if (result.StorePackages is { Length: > 0 }) facts.Add("Store packages", result.StorePackages.Length.ToString());
        if (result.BenchmarkGates is { Length: > 0 }) facts.Add("Benchmark gates", result.BenchmarkGates.Length.ToString());
        facts.Add("Bytes", (result.Artefacts?.Sum(a => a.TotalBytes) ?? 0L).ToString("N0"));
        AddPath(facts, "Manifest", result.ManifestJsonPath);
        AddPath(facts, "Run report", result.RunReportPath);
        AddPath(facts, "Run report MD", result.RunReportMarkdownPath);

        var failure = result.Failure;
        if (!result.Succeeded && failure is not null)
        {
            facts.Add("Step", Markup.Escape($"{failure.StepKind} ({failure.StepKey})"));
            AddPath(facts, "Log", failure.LogPath);
        }

        var tail = failure is null ? null : !string.IsNullOrWhiteSpace(failure.StdErrTail) ? failure.StdErrTail : failure.StdOutTail;
        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            // The output tail below carries the details; the error row keeps only the headline then.
            var message = string.IsNullOrWhiteSpace(tail) ? result.ErrorMessage! : FirstLine(result.ErrorMessage!);
            facts.Add("Error", "[fail]" + Markup.Escape(message) + "[/]");
        }

        var blocks = new List<IWidget>
        {
            new Panel(facts, "Summary") { Verdict = true, Status = result.Succeeded ? StatusKind.Pass : StatusKind.Fail }
        };
        if (!result.Succeeded && !string.IsNullOrWhiteSpace(tail))
            blocks.Add(new Panel(TextBlock.Plain(tail!.TrimEnd()), string.IsNullOrWhiteSpace(failure!.StdErrTail) ? "stdout" : "stderr"));

        if (result.Artefacts is { Length: > 0 } artefacts)
        {
            var table = Table.For(artefacts)
                .Column("Target", a => a.Target)
                .Column("Framework", a => a.Framework)
                .Column("Runtime", a => a.Runtime)
                .Column("Style", a => a.Style.ToString())
                .Column("Output", a => a.OutputDir)
                .Column("Zip", a => a.ZipPath ?? string.Empty);
            blocks.Add(table);
        }

        ConsoleOutput.Write(new Rows(blocks.ToArray()) { Gap = 1 });
    }

    private static void AddPath(KeyValueList facts, string label, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) facts.Add(label, Markup.Escape(path!));
    }

    private static string FirstLine(string message)
        => message.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
}
