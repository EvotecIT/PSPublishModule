using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Small consistent vector symbols for workspace nodes, independent of installed icon fonts.</summary>
public sealed class ProjectIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<ProjectIcon, string>(nameof(Kind), "file");
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    static ProjectIcon() => AffectsRender<ProjectIcon>(KindProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24));
        var ink = new Pen(Brush.Parse("#26334B"), 1.5);
        if (Kind == "favorite")
            context.DrawGeometry(Brush.Parse("#FFD569"), new Pen(Brush.Parse("#EBA200"), 1.2),
                Geometry.Parse("M12,2 L15,8 22,9 17,14 18,22 12,18 6,22 7,14 2,9 9,8 Z"));
        else if (Kind == "archive")
        {
            context.DrawRectangle(Brush.Parse("#F4F7FB"), ink, new Rect(4,7,16,13), 1, 1);
            context.DrawRectangle(Brush.Parse("#DDE6F1"), ink, new Rect(3,4,18,4), 1, 1);
            context.DrawLine(ink, new Point(9,12), new Point(15,12));
        }
        else if (Kind is "folder" or "project")
        {
            var accent = Kind == "project" ? "#EBA200" : "#0873FF";
            context.DrawGeometry(Brush.Parse(Kind == "project" ? "#FFD569" : "#61A6FF"), new Pen(Brush.Parse(accent), 1.5),
                Geometry.Parse("M3,5 L9,5 11,7 21,7 21,20 3,20 Z"));
            context.DrawRectangle(Brush.Parse(Kind == "project" ? "#FFE7A0" : "#A0CFFF"), new Pen(Brush.Parse(accent), 1.5), new Rect(3,9,18,11), 1, 1);
        }
        else if (Kind == "branch")
        {
            context.DrawGeometry(null, ink, Geometry.Parse("M7,5 L7,19 M7,13 L12,13 Q17,13 17,8 L17,5"));
            context.DrawEllipse(Brushes.White, ink, new Point(7,4), 2, 2);
            context.DrawEllipse(Brushes.White, ink, new Point(7,20), 2, 2);
            context.DrawEllipse(Brush.Parse("#B3E2BD"), ink, new Point(17,4), 2, 2);
        }
        else if (Kind == "script")
        {
            var gearInk = new Pen(Brush.Parse("#354C73"), 1.4, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(Brush.Parse("#EAF2FF"), gearInk, Geometry.Parse(
                "M10,2 L14,2 14.7,4.7 16.5,5.5 18.9,4.1 21,7.1 19.1,9.1 19.4,11 22,12 19.4,13 19.1,14.9 21,16.9 18.9,19.9 16.5,18.5 14.7,19.3 14,22 10,22 9.3,19.3 7.5,18.5 5.1,19.9 3,16.9 4.9,14.9 4.6,13 2,12 4.6,11 4.9,9.1 3,7.1 5.1,4.1 7.5,5.5 9.3,4.7 Z"));
            context.DrawEllipse(Brushes.White, gearInk, new Point(12, 12), 3, 3);
        }
        else if (Kind == "solution")
            context.DrawGeometry(Brush.Parse("#964AC1"), null, Geometry.Parse("M3,8 L8,4 13,9 21,2 21,22 13,15 8,20 3,16 Z M6,9 L6,15 10,12 Z"));
        else
        {
            context.DrawGeometry(Brushes.White, Kind == "markdown" ? new Pen(Brush.Parse("#0865FF"),1.5) : ink,
                Geometry.Parse("M5,2 L14,2 19,7 19,22 5,22 Z M14,2 L14,8 19,8"));
            var detail = Kind switch
            {
                "markdown" => "M8,17 L8,10 12,14 16,10 16,17",
                "json" => "M10,11 L9,11 9,14 8,15 9,16 9,19 10,19 M14,11 L15,11 15,14 16,15 15,16 15,19 14,19",
                "script" => "M8,11 L12,14 8,17 M13,18 L16,18",
                _ => "M8,12 L16,12 M8,16 L14,16"
            };
            context.DrawGeometry(null, ink, Geometry.Parse(detail));
        }
    }
}
