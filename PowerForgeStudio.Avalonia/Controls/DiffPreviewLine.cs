using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>A display-only line from a Git patch. The raw patch remains available separately.</summary>
public sealed record DiffPreviewLine(string Text, DiffPreviewLineKind Kind)
{
    private static readonly IBrush NormalForeground = Brush.Parse("#223047");
    private static readonly IBrush MetadataForeground = Brush.Parse("#586B85");
    private static readonly IBrush HunkForeground = Brush.Parse("#2455A0");
    private static readonly IBrush AdditionForeground = Brush.Parse("#17663A");
    private static readonly IBrush RemovalForeground = Brush.Parse("#A73036");
    private static readonly IBrush NormalBackground = Brushes.Transparent;
    private static readonly IBrush MetadataBackground = Brush.Parse("#F2F5F9");
    private static readonly IBrush HunkBackground = Brush.Parse("#EAF2FF");
    private static readonly IBrush AdditionBackground = Brush.Parse("#EAF8EF");
    private static readonly IBrush RemovalBackground = Brush.Parse("#FFF0F0");

    public IBrush Foreground => Kind switch
    {
        DiffPreviewLineKind.Metadata => MetadataForeground,
        DiffPreviewLineKind.Hunk => HunkForeground,
        DiffPreviewLineKind.Addition => AdditionForeground,
        DiffPreviewLineKind.Removal => RemovalForeground,
        _ => NormalForeground
    };

    public IBrush Background => Kind switch
    {
        DiffPreviewLineKind.Metadata => MetadataBackground,
        DiffPreviewLineKind.Hunk => HunkBackground,
        DiffPreviewLineKind.Addition => AdditionBackground,
        DiffPreviewLineKind.Removal => RemovalBackground,
        _ => NormalBackground
    };
}

public enum DiffPreviewLineKind { Context, Metadata, Hunk, Addition, Removal }
