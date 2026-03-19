using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using OfficeIMO.MarkdownRenderer;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Wpf.Themes;

namespace PowerForgeStudio.Wpf.Views;

public partial class GitHubThreadPreviewControl : UserControl
{
    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(
            nameof(Entries),
            typeof(IEnumerable<GitHubThreadEntry>),
            typeof(GitHubThreadPreviewControl),
            new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty DocumentTitleProperty =
        DependencyProperty.Register(
            nameof(DocumentTitle),
            typeof(string),
            typeof(GitHubThreadPreviewControl),
            new PropertyMetadata("GitHub thread", OnContentChanged));

    public static readonly DependencyProperty BaseHrefProperty =
        DependencyProperty.Register(
            nameof(BaseHref),
            typeof(string),
            typeof(GitHubThreadPreviewControl),
            new PropertyMetadata(string.Empty, OnContentChanged));

    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly ThemeService? _themeService;
    private TaskCompletionSource<bool>? _navigationCompletionSource;
    private bool _isThemeSubscribed;
    private bool _pendingShellReload = true;
    private bool _pendingBodyReload = true;
    private bool _webViewReady;

    public GitHubThreadPreviewControl()
    {
        InitializeComponent();
        _themeService = (Application.Current as App)?.ThemeService;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public IEnumerable<GitHubThreadEntry>? Entries
    {
        get => (IEnumerable<GitHubThreadEntry>?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
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

    private static async void OnContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is GitHubThreadPreviewControl control)
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

                ShowStatus("Loading discussion preview...");
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
            ShowStatus($"Discussion preview unavailable: {exception.Message}");
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

        var bodyHtml = BuildThreadHtml(options);
        Browser.CoreWebView2.PostWebMessageAsString(bodyHtml);
    }

    private MarkdownRendererOptions CreateRendererOptions()
    {
        var options = MarkdownRendererPresets.CreateRelaxed(string.IsNullOrWhiteSpace(BaseHref) ? null : BaseHref.Trim());
        options.ShellCss = BuildThemeCss();
        return options;
    }

    private string BuildThreadHtml(MarkdownRendererOptions options)
    {
        var entries = Entries?.ToList() ?? [];
        if (entries.Count == 0)
        {
            return MarkdownRenderer.RenderBodyHtml("_No discussion available._", options);
        }

        var builder = new StringBuilder();
        builder.AppendLine("<div class=\"pf-thread\">");

        foreach (var entry in entries)
        {
            builder.AppendLine("<section class=\"pf-thread-card\">");
            builder.AppendLine("<header class=\"pf-thread-card__header\">");
            builder.Append("<span class=\"pf-thread-card__badge\">");
            builder.Append(WebUtility.HtmlEncode(entry.BadgeText));
            builder.AppendLine("</span>");
            builder.Append("<div class=\"pf-thread-card__title\">");
            builder.Append(WebUtility.HtmlEncode(entry.Title));
            builder.AppendLine("</div>");
            builder.Append("<div class=\"pf-thread-card__meta\">");
            builder.Append(WebUtility.HtmlEncode(entry.AuthorLogin ?? "Unknown actor"));
            builder.Append(" • ");
            builder.Append(WebUtility.HtmlEncode(entry.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                builder.Append(" • ");
                builder.Append(WebUtility.HtmlEncode(entry.Path));
            }

            if (!string.IsNullOrWhiteSpace(entry.HtmlUrl))
            {
                builder.Append(" • ");
                builder.Append("<a href=\"");
                builder.Append(WebUtility.HtmlEncode(entry.HtmlUrl));
                builder.Append("\">Open on GitHub</a>");
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</header>");
            builder.AppendLine("<div class=\"pf-thread-card__body\">");
            builder.AppendLine(MarkdownRenderer.RenderBodyHtml(entry.Markdown ?? string.Empty, options));
            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</div>");
        return builder.ToString();
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
.pf-thread {
  padding: 16px;
}
.pf-thread-card {
  margin: 0 0 14px;
  border: 1px solid var(--pf-border);
  border-radius: 14px;
  overflow: hidden;
  background: var(--pf-surface);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.14);
}
.pf-thread-card__header {
  padding: 14px 16px 10px;
  border-bottom: 1px solid var(--pf-border);
  background: linear-gradient(180deg, rgba(59,130,246,0.08), transparent);
}
.pf-thread-card__badge {
  display: inline-block;
  padding: 4px 9px;
  border-radius: 999px;
  background: rgba(59,130,246,0.16);
  color: var(--pf-accent);
  font-size: 11px;
  font-weight: 600;
}
.pf-thread-card__title {
  margin-top: 10px;
  color: var(--pf-text-strong);
  font-size: 18px;
  font-weight: 600;
}
.pf-thread-card__meta {
  margin-top: 6px;
  color: var(--pf-text-muted);
  font-size: 12px;
}
.pf-thread-card__meta a {
  color: var(--pf-accent);
}
.pf-thread-card__body {
  background: var(--pf-surface);
}
.pf-thread-card__body .markdown-body {
  box-sizing: border-box;
  max-width: none !important;
  min-height: auto !important;
  margin: 0 !important;
  padding: 16px 18px 18px;
  background: transparent !important;
  color: var(--pf-text) !important;
}
.pf-thread-card__body .markdown-body h1,
.pf-thread-card__body .markdown-body h2,
.pf-thread-card__body .markdown-body h3,
.pf-thread-card__body .markdown-body h4,
.pf-thread-card__body .markdown-body h5,
.pf-thread-card__body .markdown-body h6 {
  color: var(--pf-text-strong) !important;
  border-bottom-color: var(--pf-border) !important;
}
.pf-thread-card__body .markdown-body p,
.pf-thread-card__body .markdown-body li,
.pf-thread-card__body .markdown-body td,
.pf-thread-card__body .markdown-body th,
.pf-thread-card__body .markdown-body blockquote {
  color: var(--pf-text) !important;
}
.pf-thread-card__body .markdown-body a {
  color: var(--pf-accent) !important;
}
.pf-thread-card__body .markdown-body hr,
.pf-thread-card__body .markdown-body table th,
.pf-thread-card__body .markdown-body table td {
  border-color: var(--pf-border) !important;
}
.pf-thread-card__body .markdown-body table tr {
  background: transparent !important;
}
.pf-thread-card__body .markdown-body table tr:nth-child(2n) {
  background: var(--pf-surface-alt) !important;
}
.pf-thread-card__body .markdown-body pre,
.pf-thread-card__body .markdown-body code,
.pf-thread-card__body .markdown-body blockquote {
  background: var(--pf-surface-alt) !important;
}
.pf-thread-card__body .markdown-body pre {
  border: 1px solid var(--pf-border);
}
.pf-thread-card__body .markdown-body blockquote {
  border-left-color: var(--pf-border) !important;
  color: var(--pf-text-muted) !important;
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
                new InvalidOperationException($"Thread shell navigation failed: {e.WebErrorStatus}."));
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
