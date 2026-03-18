using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PowerForgeStudio.Wpf.Themes;

public sealed class ThemeService : IDisposable
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";
    private const string SettingsKey = "PowerForgeStudio_AppThemeMode";

    private ResourceDictionary? _currentThemeDictionary;
    private AppThemeMode _currentMode = AppThemeMode.Auto;
    private ThemeDefinition _activeTheme;
    private bool _disposed;

    public ThemeService()
    {
        _activeTheme = ThemeDefinition.Dark;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public event Action<ThemeDefinition>? ThemeChanged;

    public AppThemeMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode == value) return;
            _currentMode = value;
            ApplyTheme();
            SavePreference(value);
        }
    }

    public ThemeDefinition ActiveTheme => _activeTheme;

    public void Initialize()
    {
        _currentMode = LoadPreference();
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var definition = ResolveTheme(_currentMode);
        if (definition.Key == _activeTheme.Key && _currentThemeDictionary is not null)
        {
            return;
        }

        _activeTheme = definition;

        var app = Application.Current;
        if (app is null) return;

        // Remove old theme dictionary
        if (_currentThemeDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_currentThemeDictionary);
        }

        // Load and apply new theme
        var uri = new Uri(definition.ResourceDictionaryPath, UriKind.Relative);
        _currentThemeDictionary = new ResourceDictionary { Source = uri };
        app.Resources.MergedDictionaries.Add(_currentThemeDictionary);

        // Update title bar for all windows
        foreach (Window window in app.Windows)
        {
            SetDarkTitleBar(window, definition.IsDarkTitleBar);
        }

        ThemeChanged?.Invoke(definition);
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(RegistryValueName);
            // 0 = dark mode, 1 = light mode
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true; // Default to dark
        }
    }

    public static void SetDarkTitleBar(Window window, bool isDark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == nint.Zero) return;
            var value = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        }
        catch
        {
            // Older Windows
        }
    }

    private static ThemeDefinition ResolveTheme(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Dark => ThemeDefinition.Dark,
            AppThemeMode.Light => ThemeDefinition.Light,
            AppThemeMode.Auto => IsSystemDarkMode() ? ThemeDefinition.Dark : ThemeDefinition.Light,
            _ => ThemeDefinition.Dark
        };
    }

    private static AppThemeMode LoadPreference()
    {
        var value = Environment.GetEnvironmentVariable(SettingsKey);
        if (Enum.TryParse<AppThemeMode>(value, true, out var mode))
        {
            return mode;
        }

        // Try registry for persisted setting
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\PowerForgeStudio");
            var stored = key?.GetValue("AppThemeMode") as string;
            if (Enum.TryParse<AppThemeMode>(stored, true, out var regMode))
            {
                return regMode;
            }
        }
        catch { }

        return AppThemeMode.Auto;
    }

    private static void SavePreference(AppThemeMode mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\PowerForgeStudio");
            key.SetValue("AppThemeMode", mode.ToString());
        }
        catch { }
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed || _currentMode != AppThemeMode.Auto) return;
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Application.Current?.Dispatcher.InvokeAsync(ApplyTheme);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
