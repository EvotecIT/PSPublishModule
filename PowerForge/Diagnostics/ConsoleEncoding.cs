using System;
using System.Text;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Best-effort console encoding setup for Unicode/emoji output.
/// </summary>
public static class ConsoleEncoding
{
    private static int _settingEncoding;

    /// <summary>
    /// Sets <see cref="Console.InputEncoding"/> and <see cref="Console.OutputEncoding"/> to UTF-8 (no BOM).
    /// Does nothing when the host does not allow changing encodings.
    /// </summary>
    public static void EnsureUtf8()
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = utf8;
            Console.OutputEncoding = utf8;
        }
        catch
        {
            // Best-effort; do not throw.
        }
    }

    /// <summary>
    /// Best-effort UTF-8 console alignment for interactive Spectre rendering.
    /// </summary>
    public static void TryEnableUtf8Console(bool unicodeCapable = true)
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsErrorRedirected) return;
            if (!unicodeCapable) return;
            if (Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage) return;
            if (Interlocked.CompareExchange(ref _settingEncoding, 1, 0) != 0) return;

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = utf8;
            Console.OutputEncoding = utf8;
        }
        catch
        {
            // Best effort only.
        }
        finally
        {
            Volatile.Write(ref _settingEncoding, 0);
        }
    }

    /// <summary>
    /// Returns true when the current console supports Unicode text. UTF-8 does not
    /// guarantee that its font can display emoji; use <see cref="ShouldRenderEmoji"/> for those.
    /// Set POWERFORGE_CONSOLE_MODE to ascii to explicitly select plain characters.
    /// </summary>
    public static bool ShouldRenderUnicode(bool unicodeCapable = true)
    {
        TryEnableUtf8Console(unicodeCapable);
        try
        {
            return unicodeCapable && Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage
                && !string.Equals(Environment.GetEnvironmentVariable("POWERFORGE_CONSOLE_MODE"), "ascii", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns whether decorative emoji are appropriate for the current console.
    /// POWERFORGE_CONSOLE_MODE=unicode keeps Unicode borders with simpler icons;
    /// rich explicitly enables emoji on older Windows hosts. Automatic selection
    /// avoids emoji on Windows Server 2016 / Windows 10 build 1607 and earlier.
    /// Font coverage cannot be inferred from the output encoding alone.
    /// </summary>
    public static bool ShouldRenderEmoji(bool unicodeCapable = true)
        => SelectEmojiSupport(
            ShouldRenderUnicode(unicodeCapable),
            Environment.OSVersion.Platform == PlatformID.Win32NT,
            Environment.OSVersion.Version,
            Environment.GetEnvironmentVariable("POWERFORGE_CONSOLE_MODE"));

    internal static bool SelectEmojiSupport(bool unicode, bool isWindows, Version osVersion, string? mode)
    {
        if (!unicode || string.Equals(mode, "ascii", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "unicode", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(mode, "rich", StringComparison.OrdinalIgnoreCase)) return true;
        return !isWindows || osVersion.Major > 10
            || (osVersion.Major == 10 && osVersion.Build > 14393);
    }
}
