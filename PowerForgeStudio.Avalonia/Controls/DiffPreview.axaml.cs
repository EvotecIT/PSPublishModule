using Avalonia;
using Avalonia.Controls;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Shared, bounded patch viewer with a complete raw-text escape hatch.</summary>
public sealed partial class DiffPreview : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DiffPreview, string>(nameof(Text), "");
    public static readonly StyledProperty<IReadOnlyList<DiffPreviewLine>> LinesProperty =
        AvaloniaProperty.Register<DiffPreview, IReadOnlyList<DiffPreviewLine>>(nameof(Lines), []);
    public static readonly StyledProperty<bool> IsRawProperty =
        AvaloniaProperty.Register<DiffPreview, bool>(nameof(IsRaw));
    public static readonly StyledProperty<bool> ShowFormattedProperty =
        AvaloniaProperty.Register<DiffPreview, bool>(nameof(ShowFormatted), true);
    public static readonly StyledProperty<bool> IsTruncatedProperty =
        AvaloniaProperty.Register<DiffPreview, bool>(nameof(IsTruncated));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IReadOnlyList<DiffPreviewLine> Lines { get => GetValue(LinesProperty); private set => SetValue(LinesProperty, value); }
    public bool IsRaw { get => GetValue(IsRawProperty); set => SetValue(IsRawProperty, value); }
    public bool ShowFormatted { get => GetValue(ShowFormattedProperty); private set => SetValue(ShowFormattedProperty, value); }
    public bool IsTruncated { get => GetValue(IsTruncatedProperty); private set => SetValue(IsTruncatedProperty, value); }

    static DiffPreview()
    {
        TextProperty.Changed.AddClassHandler<DiffPreview>((view, _) => view.UpdateLines());
        IsRawProperty.Changed.AddClassHandler<DiffPreview>((view, _) => view.ShowFormatted = !view.IsRaw);
    }

    public DiffPreview()
    {
        InitializeComponent();
        UpdateLines();
    }

    private void UpdateLines()
    {
        var presentation = DiffPreviewPresentation.Create(Text);
        Lines = presentation.Lines;
        IsTruncated = presentation.Truncated;
    }
}
