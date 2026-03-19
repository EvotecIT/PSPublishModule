using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OfficeIMO.MarkdownRenderer.Wpf;
using PowerForgeStudio.Wpf.Themes;

namespace PowerForgeStudio.Wpf.Views;

public partial class MarkdownPreviewControl : UserControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DocumentTitleProperty =
        DependencyProperty.Register(
            nameof(DocumentTitle),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata("Markdown", OnContentChanged));

    public static readonly DependencyProperty BaseHrefProperty =
        DependencyProperty.Register(
            nameof(BaseHref),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata(string.Empty, OnContentChanged));

    private readonly ThemeService? _themeService;
    private bool _isThemeSubscribed;

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isThemeSubscribed && _themeService is not null)
        {
            _themeService.ThemeChanged += OnThemeChanged;
            _isThemeSubscribed = true;
        }

        ApplyToPreview();
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
        ApplyToPreview();
    }

    private static void OnContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MarkdownPreviewControl control)
        {
            control.ApplyToPreview();
        }
    }

    private void ApplyToPreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        Preview.Markdown = Markdown ?? string.Empty;
        Preview.BodyHtml = string.Empty;
        Preview.DocumentTitle = string.IsNullOrWhiteSpace(DocumentTitle) ? "Markdown" : DocumentTitle;
        Preview.BaseHref = BaseHref ?? string.Empty;
        Preview.Preset = MarkdownViewPreset.Relaxed;
        Preview.ShellCss = BuildThemeCss();
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
}
