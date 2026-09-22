using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.Controls;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class DiffPreviewTests
{
    private const string SamplePatch = "diff --git a/readme.md b/readme.md\n--- a/readme.md\n+++ b/readme.md\n@@ -1 +1 @@\n-old\n+new\n";

    [Fact]
    public void StyledPreviewClassifiesPatchLinesWithoutChangingRawText()
    {
        var (lines, truncated) = DiffPreviewPresentation.Create(SamplePatch);
        Assert.False(truncated);
        Assert.Equal([DiffPreviewLineKind.Metadata, DiffPreviewLineKind.Metadata, DiffPreviewLineKind.Metadata,
            DiffPreviewLineKind.Hunk, DiffPreviewLineKind.Removal, DiffPreviewLineKind.Addition], lines.Select(line => line.Kind));
        Assert.Equal("+new", lines[^1].Text);

        var plain = DiffPreviewPresentation.Create("+literal content\n-not a deletion");
        Assert.All(plain.Lines, line => Assert.Equal(DiffPreviewLineKind.Context, line.Kind));

        var longPatch = SamplePatch + string.Concat(Enumerable.Repeat(" context\n", DiffPreviewPresentation.MaximumStyledLines));
        var limited = DiffPreviewPresentation.Create(longPatch);
        Assert.True(limited.Truncated);
        Assert.Equal(DiffPreviewPresentation.MaximumStyledLines, limited.Lines.Count);

        var longLine = DiffPreviewPresentation.Create("@@ -1 +1 @@\n+" + new string('x', DiffPreviewPresentation.MaximumStyledLineLength + 1));
        Assert.True(longLine.Truncated);
        Assert.True(longLine.Lines[^1].Text.Length < DiffPreviewPresentation.MaximumStyledLineLength + 10);
    }

    [Fact]
    public async Task ViewerShowsStyledLinesAndCompleteSelectableRawPatch()
    {
        await TestAppBuilder.RunAsync(() =>
        {
            var viewer = new DiffPreview { Text = SamplePatch };
            var window = new Window { Content = viewer, Width = 700, Height = 320 };
            window.Show();
            try
            {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Contains(viewer.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block.Text == "+new" && block.IsEffectivelyVisible);
                Capture(window, "diff-preview-styled.png");
                var rawToggle = Assert.Single(viewer.GetVisualDescendants().OfType<ToggleButton>());
                rawToggle.IsChecked = true;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.True(viewer.IsRaw);
                Assert.False(viewer.ShowFormatted);
                Assert.Contains(viewer.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block.Text == SamplePatch && block.IsEffectivelyVisible);
                Capture(window, "diff-preview-raw.png");
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        });
    }

    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }
}
