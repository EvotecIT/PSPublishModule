using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Consistent outline navigation symbols; no platform icon font is required.</summary>
public sealed class NavigationIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<NavigationIcon, string>(nameof(Kind), "projects");
    public static readonly StyledProperty<IBrush> InkProperty = AvaloniaProperty.Register<NavigationIcon, IBrush>(nameof(Ink), Brushes.White);
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    private static readonly IReadOnlyDictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["projects"] = "M3,5 L9,5 11,7 21,7 21,21 3,21 Z",
        ["activity"] = "M2,13 L6,13 9,4 13,21 16,10 18,13 22,13",
        ["github"] = "M7,5 L7,19 M7,12 L13,12 Q18,12 18,7 M7,3 A2,2 0 1 0 7,7 A2,2 0 1 0 7,3 M7,17 A2,2 0 1 0 7,21 A2,2 0 1 0 7,17 M18,3 A2,2 0 1 0 18,7 A2,2 0 1 0 18,3",
        ["schedules"] = "M3,6 L21,6 21,21 3,21 Z M3,11 L21,11 M8,3 L8,8 M16,3 L16,8",
        ["storage"] = "M3,6 C3,1 21,1 21,6 C21,11 3,11 3,6 L3,18 C3,23 21,23 21,18 L21,6 M3,12 C3,17 21,17 21,12",
        ["connections"] = "M8,4 L8,9 M16,4 L16,9 M5,9 L19,9 19,12 Q19,18 12,18 Q5,18 5,12 Z M12,18 L12,23",
        ["settings"] = "M12,3 A3,3 0 1 0 12,9 A3,3 0 1 0 12,3 M4,11 L4,15 M20,11 L20,15 M8,19 L10,22 14,22 16,19 M8,5 L5,7 3,10 4,12 M16,5 L19,7 21,10 20,12 M4,15 L6,18 8,19 M20,15 L18,18 16,19 M12,9 L12,16 M9,13 L15,13",
        ["search"] = "M16,16 L22,22 M18,10 A8,8 0 1 0 2,10 A8,8 0 1 0 18,10",
        ["refresh"] = "M20,9 A8,8 0 1 0 20,16 M20,3 L20,9 14,9",
        ["plus"] = "M12,4 L12,20 M4,12 L20,12",
        ["menu"] = "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18",
        ["monitor"] = "M2,3 L22,3 22,17 2,17 Z M12,17 L12,22 M7,22 L17,22",
        ["output"] = "M3,4 L21,4 21,20 3,20 Z M7,9 L10,12 7,15 M13,15 L17,15"
    }.ToDictionary(pair => pair.Key, pair => Geometry.Parse(pair.Value));
    static NavigationIcon() => AffectsRender<NavigationIcon>(KindProperty, InkProperty);
    public override void Render(DrawingContext context)
    {
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24));
        context.DrawGeometry(null, new Pen(Ink, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            Shapes.GetValueOrDefault(Kind, Shapes["projects"]));
    }
}
