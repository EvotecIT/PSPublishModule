using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf.Views;

public partial class TerminalControl : UserControl
{
    private TerminalTabViewModel? _viewModel;
    private bool _webViewReady;

    public TerminalControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.OutputAvailable -= OnOutputAvailable;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as TerminalTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.OutputAvailable += OnOutputAvailable;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _ = InitializeWebViewAsync();
        }
    }

    private async Task InitializeWebViewAsync()
    {
        if (_webViewReady) return;

        try
        {
            LoadingText.Visibility = Visibility.Visible;
            WebView.Visibility = Visibility.Collapsed;

            await WebView.EnsureCoreWebView2Async().ConfigureAwait(true);

            var settings = WebView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;

            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "terminal.html");
            if (File.Exists(htmlPath))
            {
                WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            }
            else
            {
                LoadingText.Text = $"terminal.html not found at: {htmlPath}";
                return;
            }

            // Wait for xterm.js to fully initialize
            await Task.Delay(500).ConfigureAwait(true);

            _webViewReady = true;
            LoadingText.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;

            // NOW start the ConPTY session — WebView2 is ready to receive output
            _viewModel?.StartSession();
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Terminal init failed: {ex.Message}";
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_viewModel is null) return;

        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "input":
                    var b64 = doc.RootElement.GetProperty("data").GetString();
                    if (b64 is not null)
                    {
                        _viewModel.SendInput(Convert.FromBase64String(b64));
                    }
                    break;

                case "resize":
                    var cols = doc.RootElement.GetProperty("cols").GetInt32();
                    var rows = doc.RootElement.GetProperty("rows").GetInt32();
                    _viewModel.Resize(cols, rows);
                    break;
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private void OnOutputAvailable(byte[] data)
    {
        if (!_webViewReady) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!_webViewReady || WebView.CoreWebView2 is null) return;
            try
            {
                WebView.CoreWebView2.PostWebMessageAsString(Convert.ToBase64String(data));
            }
            catch
            {
                // WebView may have been disposed
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalTabViewModel.IsConnected) && _viewModel?.IsConnected == false)
        {
            Dispatcher.InvokeAsync(() => ExitedOverlay.Visibility = Visibility.Visible);
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        ExitedOverlay.Visibility = Visibility.Collapsed;
        _viewModel?.StartSession();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.OutputAvailable -= OnOutputAvailable;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_webViewReady && WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
    }
}
