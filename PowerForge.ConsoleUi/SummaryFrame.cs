using System;
using System.Collections.Generic;
using ConsoleMonster.Frames;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;

namespace PowerForge.ConsoleUi;

/// <summary>
/// The one layout for the module build, project build and release summaries: a ConsoleMonster
/// <see cref="DashboardFrame"/> with the outcome on the title line, headline meters, a "Needs attention" list counted
/// by an <see cref="AttentionTally"/>, an overview list and detail sections. A failure follows the frame as an
/// <see cref="ExceptionView"/>.
/// </summary>
internal sealed class SummaryFrame
{
    private readonly DashboardFrame _frame;
    private readonly List<IWidget> _after = new();

    internal SummaryFrame(string title, bool success, string? outcome = null)
    {
        _frame = new DashboardFrame(title);
        _frame.Header.States.Add(success
            ? FrameState.Healthy(outcome ?? "succeeded")
            : FrameState.Of(HealthState.Failing, 1, outcome ?? "failed"));
        _frame.Footer.Hint = FrameHint.None;
    }

    /// <summary>Counts findings as the summary is built; they are listed under "Needs attention".</summary>
    internal AttentionTally Attention { get; } = new();

    /// <summary>The overview: one line per area, printed with the summary sections.</summary>
    internal KeyValueList Overview { get; } = new();

    internal SummaryFrame Fact(string? fact)
    {
        if (!string.IsNullOrWhiteSpace(fact)) _frame.Header.Facts.Add(fact!);
        return this;
    }

    internal SummaryFrame Meter(Meter meter)
    {
        _frame.Meters.Add(meter);
        return this;
    }

    /// <summary>Adds an overview line; <paramref name="value"/> is plain text.</summary>
    internal SummaryFrame Line(string label, string? value)
    {
        Overview.Add(label, Markup.Escape(value ?? string.Empty));
        return this;
    }

    /// <summary>Adds an overview line whose value is markup.</summary>
    internal SummaryFrame LineMarkup(string label, string markup)
    {
        Overview.Add(label, markup);
        return this;
    }

    /// <summary>Adds a detail section, printed below the summary rows.</summary>
    internal SummaryFrame Detail(string title, IWidget content)
    {
        _frame.Sections.Add(new FrameSection(title, _ => content) { Placement = FramePlacement.Detail, FullWidth = true });
        return this;
    }

    /// <summary>Adds a widget printed after the frame, such as a list of paths.</summary>
    internal SummaryFrame After(IWidget widget)
    {
        _after.Add(widget);
        return this;
    }

    /// <summary>Writes the frame, then the failure (if any) with its source context and inner exceptions.</summary>
    internal void Write(Exception? failure = null)
    {
        if (Overview.Items.Count > 0)
            _frame.Sections.Insert(0, new FrameSection("Overview", _ => Overview) { FullWidth = true });
        _frame.Attention.Noun = "finding";
        Attention.AddTo(_frame.Attention);

        var blocks = new List<IWidget> { _frame };
        blocks.AddRange(_after);
        if (failure is not null) blocks.Add(new ExceptionView(failure) { MaxFrames = 6 });
        ConsoleOutput.Write(new Rows(blocks.ToArray()) { Gap = 1 });
    }

    internal static HealthState From(CheckStatus status) => status switch
    {
        CheckStatus.Pass => HealthState.Healthy,
        CheckStatus.Warning => HealthState.Warning,
        _ => HealthState.Failing
    };

    internal static string Word(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "[pass]Pass[/]",
        CheckStatus.Warning => "[warn]Warning[/]",
        _ => "[fail]Fail[/]"
    };
}
