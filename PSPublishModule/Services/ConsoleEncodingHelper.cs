using System;
using System.Threading;
using System.Text;
using Spectre.Console;

namespace PSPublishModule;

internal static class ConsoleEncodingHelper
{
    // Prevent concurrent attempts to change console encoding.
    private static int _settingEncoding;

    /// <summary>
    /// Ensures the console output encoding is UTF-8 when we render Unicode via Spectre.Console.
    /// If the console is not interactive or the host doesn't support changing the encoding,
    /// this method is best-effort and will silently do nothing.
    /// </summary>
    public static void TryEnableUtf8Console()
    {
        try
        {
            // If output is redirected, do not change global console settings.
            if (Console.IsOutputRedirected || Console.IsErrorRedirected) return;

            // If Spectre is going to render Unicode, align the console encoding to avoid mojibake.
            if (!AnsiConsole.Profile.Capabilities.Unicode) return;

            if (Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage) return;

            if (Interlocked.CompareExchange(ref _settingEncoding, 1, 0) != 0) return;

            // No BOM on console output.
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = utf8;
            Console.InputEncoding = utf8;
        }
        catch
        {
            // Best-effort only. Some hosts don't allow changing Console encodings.
        }
        finally
        {
            Volatile.Write(ref _settingEncoding, 0);
        }
    }

    public static bool ShouldRenderUnicode()
    {
        // Do not cache this: some steps (external tools / other modules) can change the console encoding mid-run.
        TryEnableUtf8Console();
        try
        {
            if (!AnsiConsole.Profile.Capabilities.Unicode) return false;
            return Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage;
        }
        catch
        {
            return false;
        }
    }
}
