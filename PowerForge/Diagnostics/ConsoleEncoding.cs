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
    /// Returns true when the current console can safely render Unicode output.
    /// </summary>
    public static bool ShouldRenderUnicode(bool unicodeCapable = true)
    {
        TryEnableUtf8Console(unicodeCapable);
        try
        {
            return unicodeCapable && Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage;
        }
        catch
        {
            return false;
        }
    }
}
