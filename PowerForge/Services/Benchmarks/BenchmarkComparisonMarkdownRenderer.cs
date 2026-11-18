using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>Selects comparisons and the run to show when rendering README tables from an evidence catalog.</summary>
public sealed class BenchmarkComparisonMarkdownRequest
{
    /// <summary>Comparison identifiers or group names; every comparison in a named group is included.</summary>
    public string[] Comparisons { get; set; } = Array.Empty<string>();

    /// <summary>Platform whose published run is shown, such as windows.</summary>
    public string Platform { get; set; } = "windows";

    /// <summary>Optional target framework; when omitted, the newest published run on the platform is shown.</summary>
    public string? Framework { get; set; }
}

/// <summary>
/// Renders README tables from an evidence catalog view. Only published runs are shown, because a README makes
/// public claims; libraries whose results are not published appear only as a note.
/// </summary>
public sealed class BenchmarkComparisonMarkdownRenderer
{
    /// <summary>Renders the selected comparisons.</summary>
    /// <param name="view">Catalog view from <see cref="BenchmarkComparisonViewBuilder"/>.</param>
    /// <param name="request">Comparisons, platform and framework to show.</param>
    /// <returns>Markdown for a benchmark block.</returns>
    public string Render(BenchmarkComparisonCatalogView view, BenchmarkComparisonMarkdownRequest request)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (request is null) throw new ArgumentNullException(nameof(request));
        string[] tokens = (request.Comparisons ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        if (tokens.Length == 0)
            throw new ArgumentException("Name at least one comparison or group.", nameof(request));
        string[] unknown = tokens.Where(token => !view.Comparisons.Any(comparison => Matches(comparison, token))).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"No comparison or group named {string.Join(", ", unknown.Select(value => $"'{value}'"))} in the catalog.");

        string platform = (request.Platform ?? string.Empty).Trim().ToLowerInvariant();
        var builder = new StringBuilder();
        foreach (BenchmarkComparisonView comparison in view.Comparisons.Where(comparison => tokens.Any(token => Matches(comparison, token))))
        {
            if (builder.Length > 0)
                builder.AppendLine();
            RenderComparison(builder, comparison, platform, request.Framework?.Trim());
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static bool Matches(BenchmarkComparisonView comparison, string token)
        => string.Equals(comparison.Id, token, StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(comparison.Group) && string.Equals(comparison.Group, token, StringComparison.OrdinalIgnoreCase));

    private static void RenderComparison(StringBuilder builder, BenchmarkComparisonView comparison, string platform, string? framework)
    {
        builder.Append("#### ").AppendLine(Escape(comparison.Title)).AppendLine();
        BenchmarkRunView? run = comparison.Runs
            .Where(value => value.Published &&
                            string.Equals(value.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrWhiteSpace(framework) || string.Equals(value.Framework, framework, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(value => value.IsFull ? 0 : 1)
            .ThenByDescending(value => value.GeneratedUtc)
            .FirstOrDefault();
        if (run is null)
        {
            string target = string.IsNullOrWhiteSpace(framework) ? platform : platform + " " + framework;
            builder.Append("_No published ").Append(Escape(target)).AppendLine(" run yet._");
            return;
        }

        var facts = new List<string> { run.PlatformLabel };
        if (run.Framework.Length > 0) facts.Add(run.Framework);
        facts.Add(run.ProfileText);
        facts.Add(run.Date);
        if (run.Environment.Text.Length > 0) facts.Add(run.Environment.Text);
        builder.AppendLine(Escape(string.Join(" · ", facts))).AppendLine();

        builder.Append("| Case | ").Append(string.Join(" | ", run.Contenders.Select(contender => Escape(contender.Name)))).AppendLine(" |");
        builder.Append("| --- |").Append(string.Concat(run.Contenders.Select(_ => " ---: |"))).AppendLine();
        foreach (BenchmarkCaseView row in run.Cases)
        {
            builder.Append("| ").Append(Escape(row.Label.Length > 0 ? row.Label : comparison.Title)).Append(" |");
            foreach (BenchmarkCellView cell in row.Cells)
                builder.Append(' ').Append(CellText(cell)).Append(" |");
            builder.AppendLine();
        }
        builder.AppendLine();

        foreach (BenchmarkHeadToHeadView headToHead in run.HeadToHead.Where(value => value.Text.Length > 0))
            builder.Append("- **").Append(Escape(headToHead.Text)).Append("** (").Append(Escape(headToHead.Detail)).AppendLine(")");
        if (run.Statistic.Length > 0)
            builder.Append("- Times are ").Append(run.Statistic).AppendLine("s; results whose confidence intervals overlap are reported as even.");
        foreach (BenchmarkNotPublishedView dropped in run.NotPublished.Where(value => value.Licence))
            builder.Append("- ").Append(Escape(dropped.Library)).AppendLine(": not published (licence).");
        if (run.Runner.Text.Length > 0)
            builder.Append("- Runner: ").AppendLine(Escape(run.Runner.Text));
    }

    private static string CellText(BenchmarkCellView cell)
    {
        if (cell.Missing) return "—";
        if (cell.Failed) return "Failed";
        string time = Escape(cell.TimeText);
        return cell.Relative.IsFastest || string.IsNullOrWhiteSpace(cell.Relative.Text) || cell.Relative.Text == "—"
            ? "**" + time + "**"
            : time + " (" + Escape(cell.Relative.Text) + ")";
    }

    private static string Escape(string? text)
        => (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
