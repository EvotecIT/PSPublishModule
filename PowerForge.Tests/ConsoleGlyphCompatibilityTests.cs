using System.Collections.Concurrent;
using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class ConsoleGlyphCompatibilityTests
{
    [Theory]
    [InlineData(true, true, 10, 14393, null, false)]
    [InlineData(true, true, 6, 9600, null, false)]
    [InlineData(true, true, 10, 20348, null, true)]
    [InlineData(true, false, 6, 0, null, true)]
    [InlineData(true, true, 10, 14393, "rich", true)]
    [InlineData(true, true, 10, 20348, "unicode", false)]
    [InlineData(true, true, 10, 20348, "ASCII", false)]
    [InlineData(false, true, 10, 20348, "rich", false)]
    public void EmojiSelection_DistinguishesEncodingFromFontCompatibility(
        bool unicode, bool windows, int major, int build, string? mode, bool expected)
        => Assert.Equal(expected, ConsoleEncoding.SelectEmojiSupport(
            unicode, windows, new Version(major, 0, build), mode));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompatibleProgress_UsesAsciiSpinnerWithoutDisablingUnicodeText(bool emoji)
    {
        var columns = SpectreBuildProgressColumns.CreateDetailed(false, true, 0,
            new ConcurrentDictionary<ProgressTask, string>(),
            new ConcurrentDictionary<ProgressTask, DateTimeOffset>(),
            new ConcurrentDictionary<ProgressTask, TimeSpan>(), emoji: emoji);
        var spinner = Assert.IsType<SpinnerColumn>(columns.Last()).Spinner;
        Assert.Same(emoji ? Spinner.Known.Dots : Spinner.Known.Line, spinner);
    }

    [Theory]
    [InlineData("包裹包裹包裹包裹包裹包裹包裹包裹包裹包裹包裹包裹包裹包裹")]
    [InlineData("Build 🔨 Build 🔨 Build 🔨 Build 🔨 Build 🔨 Build 🔨")]
    [InlineData("éééééééééééééééééééééééééééééé")]
    public void ProgressLabels_FitDisplayCellsWithoutSplittingTextElements(string title)
    {
        var presentation = new SpectreProgressPresentation(60, unicode: true, emoji: false);
        var label = presentation.BuildLabel(new SpectreProgressLedgerItem { Title = title + title }, null);
        // At 60 cells, the shared columns reserve 29 cells for status, time and spacing.
        Assert.Equal(31, label.GetCellWidth());
        Assert.EndsWith("…", label.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("�", label, StringComparison.Ordinal);
        for (var i = 0; i < label.Length; i++)
        {
            if (!char.IsSurrogate(label[i])) continue;
            Assert.True(char.IsHighSurrogate(label[i]) && i + 1 < label.Length && char.IsLowSurrogate(label[++i]));
        }
        Assert.False(label.TrimEnd(' ', '…').EndsWith("e", StringComparison.Ordinal));
    }
}
