using System.Windows;
using Microsoft.Win32;

namespace PowerForgeStudio.Wpf.Themes;

public static class WindowStateService
{
    private const string RegistryPath = @"Software\PowerForgeStudio\WindowState";

    public static void SaveState(Window window, string? lastProjectName = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("Left", (int)window.Left);
            key.SetValue("Top", (int)window.Top);
            key.SetValue("Width", (int)window.Width);
            key.SetValue("Height", (int)window.Height);
            key.SetValue("Maximized", window.WindowState == WindowState.Maximized ? 1 : 0);

            if (lastProjectName is not null)
            {
                key.SetValue("LastProject", lastProjectName);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    public static void RestoreState(Window window)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key is null) return;

            var left = key.GetValue("Left") as int?;
            var top = key.GetValue("Top") as int?;
            var width = key.GetValue("Width") as int?;
            var height = key.GetValue("Height") as int?;
            var maximized = (key.GetValue("Maximized") as int?) == 1;

            if (width.HasValue && width.Value > 400 && height.HasValue && height.Value > 300)
            {
                window.Width = width.Value;
                window.Height = height.Value;
            }

            if (left.HasValue && top.HasValue && left.Value > -32000 && top.Value > -32000)
            {
                window.Left = left.Value;
                window.Top = top.Value;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            }

            if (maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    public static string? GetLastProjectName()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue("LastProject") as string;
        }
        catch
        {
            return null;
        }
    }
}
