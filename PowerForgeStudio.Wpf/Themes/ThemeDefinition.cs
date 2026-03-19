namespace PowerForgeStudio.Wpf.Themes;

public sealed record ThemeDefinition(
    string Key,
    string DisplayName,
    bool IsDarkTitleBar,
    string ResourceDictionaryPath)
{
    public static ThemeDefinition Dark { get; } = new(
        Key: "Dark",
        DisplayName: "Dark",
        IsDarkTitleBar: true,
        ResourceDictionaryPath: "Themes/DarkTheme.xaml");

    public static ThemeDefinition Light { get; } = new(
        Key: "Light",
        DisplayName: "Light",
        IsDarkTitleBar: false,
        ResourceDictionaryPath: "Themes/LightTheme.xaml");

    public static IReadOnlyList<ThemeDefinition> All { get; } = [Dark, Light];

    public static ThemeDefinition GetByKey(string key)
        => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Dark;
}
