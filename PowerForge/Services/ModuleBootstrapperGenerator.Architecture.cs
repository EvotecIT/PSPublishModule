namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static string RenderWindowsRuntimeArchitectureResolver(
        string architectureVariable,
        string architectureFolderVariable)
    {
        var fallbackFolderExpression = $"if ([IntPtr]::Size -eq 4) {{ 'win-x86' }} else {{ 'win-x64' }}";

        return RenderModuleBootstrapperTemplate(
            "WindowsRuntimeArchitectureResolver",
            "Scripts/ModuleBootstrapper/WindowsRuntimeArchitectureResolver.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArchitectureVariable"] = architectureVariable,
                ["ArchitectureFolderVariable"] = architectureFolderVariable,
                ["FallbackFolderExpression"] = fallbackFolderExpression
            });
    }
}
