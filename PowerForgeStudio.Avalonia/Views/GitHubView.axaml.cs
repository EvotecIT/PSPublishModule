using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class GitHubView : UserControl
{
    public GitHubView() => InitializeComponent();

    private async void OpenOnGitHub(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not GitHubViewModel model || !model.HasSelection || TopLevel.GetTopLevel(this) is not { } top) return;
        // Constructed from the validated repository and integer item number, never remote body/HTML links.
        if (!Uri.TryCreate(model.SelectedUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com") return;
        try
        {
            if (!await top.Launcher.LaunchUriAsync(uri)) model.DetailStatus = "Could not open the browser.";
        }
        catch { model.DetailStatus = "Could not open the browser."; }
    }
}
