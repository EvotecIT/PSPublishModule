using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Measures the explorer indentation and draws the ancestor columns for one native tree row.</summary>
public sealed class TreeAncestryGuides : Control
{
    public static readonly StyledProperty<int> LevelProperty = AvaloniaProperty.Register<TreeAncestryGuides, int>(nameof(Level));
    public int Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    static TreeAncestryGuides()
    {
        AffectsMeasure<TreeAncestryGuides>(LevelProperty);
        AffectsRender<TreeAncestryGuides>(LevelProperty);
    }
    protected override Size MeasureOverride(Size availableSize) => new(12 + Math.Max(0, Level) * 19, 30);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Brush.Parse("#E3EAF3"), 1);
        for (var level = 0; level < Level; level++)
        {
            var x = 20.5 + level * 19;
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }
}
