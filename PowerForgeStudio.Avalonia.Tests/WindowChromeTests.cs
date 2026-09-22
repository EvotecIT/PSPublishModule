using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WindowChromeTests
{
    [Fact]
    public async Task NativeTitleBandAndWindowControlsRemainUsable()
    {
        await TestAppBuilder.RunAsync(() =>
        {
            var window = new MainWindow { Width = 1600, Height = 1000 };
            window.Show();
            try
            {
                Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                Assert.Equal(WindowDecorationsElementRole.TitleBar,
                    WindowDecorationProperties.GetElementRole(window.FindControl<Border>("TitleBand")!));
                Assert.Equal(WindowDecorationsElementRole.ResizeSE,
                    WindowDecorationProperties.GetElementRole(window.FindControl<Border>("SoutheastResizeGrip")!));
                Assert.Equal(WindowDecorationsElementRole.User,
                    WindowDecorationProperties.GetElementRole(window.FindControl<TextBox>("QuickProjectSearchBox")!));

                var maximize = window.FindControl<Button>("MaximizeWindowButton")!;
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(WindowState.Maximized, window.WindowState);
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(WindowState.Normal, window.WindowState);
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        });
    }
}
