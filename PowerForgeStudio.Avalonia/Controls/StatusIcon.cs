using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Shared vector cue for evidence states, always paired with a text label.</summary>
public sealed class StatusIcon : Control
{
    public static readonly StyledProperty<bool> IsErrorProperty = AvaloniaProperty.Register<StatusIcon, bool>(nameof(IsError));
    public static readonly StyledProperty<bool> IsWarningProperty = AvaloniaProperty.Register<StatusIcon, bool>(nameof(IsWarning));
    public static readonly StyledProperty<bool> IsReviewProperty = AvaloniaProperty.Register<StatusIcon, bool>(nameof(IsReview));
    public static readonly StyledProperty<bool> IsSuccessProperty = AvaloniaProperty.Register<StatusIcon, bool>(nameof(IsSuccess));

    public bool IsError { get => GetValue(IsErrorProperty); set => SetValue(IsErrorProperty, value); }
    public bool IsWarning { get => GetValue(IsWarningProperty); set => SetValue(IsWarningProperty, value); }
    public bool IsReview { get => GetValue(IsReviewProperty); set => SetValue(IsReviewProperty, value); }
    public bool IsSuccess { get => GetValue(IsSuccessProperty); set => SetValue(IsSuccessProperty, value); }

    private static readonly IBrush CleanBrush = Brush.Parse("#168A46");
    private static readonly IBrush WarningBrush = Brush.Parse("#E6A000");
    private static readonly IBrush ErrorBrush = Brush.Parse("#D32835");
    private static readonly IBrush ReviewBrush = Brush.Parse("#1769ED");
    private static readonly IBrush NeutralBrush = Brush.Parse("#738198");
    private static readonly Pen WhitePen = new(Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    static StatusIcon() => AffectsRender<StatusIcon>(IsErrorProperty, IsWarningProperty, IsReviewProperty, IsSuccessProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 20, Bounds.Height / 20));
        context.DrawEllipse(IsError ? ErrorBrush : IsWarning ? WarningBrush : IsReview ? ReviewBrush : IsSuccess ? CleanBrush : NeutralBrush,
            null, new Point(10, 10), 8, 8);
        if (IsError || IsWarning)
        {
            context.DrawLine(WhitePen, new Point(10, 5), new Point(10, 11));
            context.DrawEllipse(Brushes.White, null, new Point(10, 14), 1, 1);
        }
        else if (IsReview || !IsSuccess)
        {
            context.DrawEllipse(Brushes.White, null, new Point(10, 10), 2.5, 2.5);
        }
        else
        {
            context.DrawLine(WhitePen, new Point(6, 10), new Point(9, 13));
            context.DrawLine(WhitePen, new Point(9, 13), new Point(14, 7));
        }
    }
}
