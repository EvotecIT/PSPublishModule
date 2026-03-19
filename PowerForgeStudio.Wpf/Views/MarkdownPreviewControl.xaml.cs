using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using OfficeIMO.MarkdownRenderer;
using PowerForgeStudio.Wpf.Themes;

namespace PowerForgeStudio.Wpf.Views;

public partial class MarkdownPreviewControl : UserControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty DocumentTitleProperty =
        DependencyProperty.Register(
            nameof(DocumentTitle),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata("Markdown", OnShellPropertyChanged));

    public static readonly DependencyProperty BaseHrefProperty =
        DependencyProperty.Register(
            nameof(BaseHref),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata(string.Empty, OnShellPropertyChanged));

    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly ThemeService? _themeService;
    private TaskCompletionSource<bool>? _navigationCompletionSource;
    private bool _isThemeSubscribed;
    private bool _pendingShellReload = true;
    private bool _pendingBodyReload = true;
    private bool _webViewReady;

    public MarkdownPreviewControl()
    {
        InitializeComponent();
        _themeService = (Application.Current as App)?.ThemeService;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string DocumentTitle
    {
        get => (string)GetValue(DocumentTitleProperty);
        set => SetValue(DocumentTitleProperty, value);
    }

    public string BaseHref
    {
        get => (string)GetValue(BaseHrefProperty);
        set => SetValue(BaseHrefProperty, value);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isThemeSubscribed && _themeService is not null)
        {
            _themeService.ThemeChanged += OnThemeChanged;
            _isThemeSubscribed = true;
        }

        await RenderPendingAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isThemeSubscribed && _themeService is not null)
        {
            _themeService.ThemeChanged -= OnThemeChanged;
            _isThemeSubscribed = false;
        }
    }

    private void OnThemeChanged(ThemeDefinition theme)
    {
        _pendingShellReload = true;
        _pendingBodyReload = true;
        _ = Dispatcher.InvokeAsync(RenderPendingAsync);
    }

    private static async void OnMarkdownChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MarkdownPreviewControl control)
        {
            control._pendingBodyReload = true;
            await control.RenderPendingAsync().ConfigureAwait(true);
        }
    }

    private static async void OnShellPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MarkdownPreviewControl control)
        {
            control._pendingShellReload = true;
            control._pendingBodyReload = true;
            await control.RenderPendingAsync().ConfigureAwait(true);
        }
    }

    private async Task RenderPendingAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        await _renderGate.WaitAsync().ConfigureAwait(true);
        try
        {
            while (_pendingShellReload || _pendingBodyReload)
            {
                var rebuildShell = _pendingShellReload || !_webViewReady;
                _pendingShellReload = false;
                _pendingBodyReload = false;

                ShowStatus("Loading markdown preview...");
                await EnsureWebViewAsync().ConfigureAwait(true);

                var options = CreateRendererOptions();
                if (rebuildShell)
                {
                    await NavigateShellAsync(options).ConfigureAwait(true);
                }

                UpdateBody(options);
                ShowBrowser();
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"Markdown preview unavailable: {exception.Message}");
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_webViewReady)
        {
            return;
        }

        await Browser.EnsureCoreWebView2Async().ConfigureAwait(true);

        var settings = Browser.CoreWebView2.Settings;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;

        Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
        Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webViewReady = true;
    }

    private async Task NavigateShellAsync(MarkdownRendererOptions options)
    {
        if (Browser.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 is not initialized.");
        }

        _navigationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Browser.NavigateToString(MarkdownRenderer.BuildShellHtml(DocumentTitle, options));
        await _navigationCompletionSource.Task.ConfigureAwait(true);
    }

    private void UpdateBody(MarkdownRendererOptions options)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var bodyHtml = MarkdownRenderer.RenderBodyHtml(Markdown ?? string.Empty, options);
        Browser.CoreWebView2.PostWebMessageAsString(bodyHtml);
    }

    private MarkdownRendererOptions CreateRendererOptions()
    {
        var options = MarkdownRendererPresets.CreateRelaxed(string.IsNullOrWhiteSpace(BaseHref) ? null : BaseHref.Trim());
        options.ShellCss = BuildThemeCss();
        return options;
    }

    private string BuildThemeCss()
    {
        var isDark = _themeService?.ActiveTheme.IsDarkTitleBar ?? true;
        var shell = GetCssColor("AppShellBrush", isDark ? "#0F1117" : "#F3F4F6");
        var surface = GetCssColor("AppSurfaceBrush", isDark ? "#161822" : "#FFFFFF");
        var surfaceAlt = GetCssColor("AppSurfaceAltBrush", isDark ? "#1C1F2E" : "#F9FAFB");
        var border = GetCssColor("AppBorderBrush", isDark ? "#2A2F45" : "#E5E7EB");
        var text = GetCssColor("AppTextBrush", isDark ? "#B8BDD0" : "#374151");
        var textStrong = GetCssColor("AppTextStrongBrush", isDark ? "#F8FAFC" : "#111827");
        var textMuted = GetCssColor("AppTextMutedBrush", isDark ? "#6B7280" : "#9CA3AF");
        var accent = GetCssColor("AppAccentBrush", isDark ? "#3B82F6" : "#2563EB");

        return $$"""
:root {
  color-scheme: {{(isDark ? "dark" : "light")}};
  --pf-shell: {{shell}};
  --pf-surface: {{surface}};
  --pf-surface-alt: {{surfaceAlt}};
  --pf-border: {{border}};
  --pf-text: {{text}};
  --pf-text-strong: {{textStrong}};
  --pf-text-muted: {{textMuted}};
  --pf-accent: {{accent}};
}
html, body {
  background: var(--pf-shell) !important;
  color: var(--pf-text) !important;
}
body {
  margin: 0;
  font-family: "Segoe UI Variable Text", "Segoe UI", sans-serif;
}
.markdown-body {
  box-sizing: border-box;
  max-width: none !important;
  margin: 0 !important;
  min-height: 100vh;
  padding: 20px 24px 28px;
  background: var(--pf-surface) !important;
  color: var(--pf-text) !important;
}
.markdown-body h1,
.markdown-body h2,
.markdown-body h3,
.markdown-body h4,
.markdown-body h5,
.markdown-body h6 {
  color: var(--pf-text-strong) !important;
  border-bottom-color: var(--pf-border) !important;
}
.markdown-body p,
.markdown-body li,
.markdown-body td,
.markdown-body th,
.markdown-body blockquote {
  color: var(--pf-text) !important;
}
.markdown-body a {
  color: var(--pf-accent) !important;
}
.markdown-body hr,
.markdown-body table th,
.markdown-body table td {
  border-color: var(--pf-border) !important;
}
.markdown-body table tr {
  background: transparent !important;
}
.markdown-body table tr:nth-child(2n) {
  background: var(--pf-surface-alt) !important;
}
.markdown-body pre,
.markdown-body code,
.markdown-body blockquote {
  background: var(--pf-surface-alt) !important;
}
.markdown-body pre {
  border: 1px solid var(--pf-border);
}
.markdown-body blockquote {
  border-left-color: var(--pf-border) !important;
  color: var(--pf-text-muted) !important;
}
.markdown-body img {
  border-radius: 10px;
}
""";
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_navigationCompletionSource is null)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _navigationCompletionSource.TrySetResult(true);
        }
        else
        {
            _navigationCompletionSource.TrySetException(
                new InvalidOperationException($"Markdown shell navigation failed: {e.WebErrorStatus}."));
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.IsUserInitiated || !TryGetExternalNavigationUri(e.Uri, out var navigationUri))
        {
            return;
        }

        e.Cancel = true;
        TryOpenExternal(navigationUri);
    }

    private string GetCssColor(string resourceKey, string fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
        {
            return ToCssColor(brush.Color);
        }

        return fallback;
    }

    private static string ToCssColor(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryGetExternalNavigationUri(string? rawUri, out Uri navigationUri)
    {
        navigationUri = null!;
        if (string.IsNullOrWhiteSpace(rawUri) || !Uri.TryCreate(rawUri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (string.Equals(parsed.Scheme, "about", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Scheme, "data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Scheme, "javascript", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        navigationUri = parsed;
        return true;
    }

    private static void TryOpenExternal(Uri navigationUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(navigationUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch failures.
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusOverlay.Visibility = Visibility.Visible;
        Browser.Visibility = Visibility.Collapsed;
    }

    private void ShowBrowser()
    {
        StatusOverlay.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }
}
