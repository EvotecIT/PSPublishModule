using System;
using System.Threading;
using System.Text;
using Spectre.Console;

namespace PSPublishModule;

internal static class ConsoleEncodingHelper
{
    // Thread-safe "run once" gate, since UI/Logger can be called from multiple threads.
    private static int _attempted;

    // Cache result of "can safely render unicode" check: -1 unknown, 0 false, 1 true.
    private static int _shouldRenderUnicode = -1;

    /// <summary>
    /// Ensures the console output encoding is UTF-8 when we render Unicode via Spectre.Console.
    /// If the console is not interactive or the host doesn't support changing the encoding,
    /// this method is best-effort and will silently do nothing.
    /// </summary>
    public static void TryEnableUtf8Console()
    {
        if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0) return;

        try
        {
            // If output is redirected, do not change global console settings.
            if (Console.IsOutputRedirected || Console.IsErrorRedirected) return;

            // If Spectre is going to render Unicode, align the console encoding to avoid mojibake.
            if (!AnsiConsole.Profile.Capabilities.Unicode) return;

            if (Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage) return;

            // No BOM on console output.
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = utf8;
            Console.InputEncoding = utf8;
        }
        catch
        {
            // Best-effort only. Some hosts don't allow changing Console encodings.
        }
    }

    public static bool ShouldRenderUnicode()
    {
        var cached = Volatile.Read(ref _shouldRenderUnicode);
        if (cached >= 0) return cached == 1;

        TryEnableUtf8Console();
        try
        {
            if (!AnsiConsole.Profile.Capabilities.Unicode) return false;
            var ok = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage;
            Interlocked.CompareExchange(ref _shouldRenderUnicode, ok ? 1 : 0, -1);
            return ok;
        }
        catch
        {
            Interlocked.CompareExchange(ref _shouldRenderUnicode, 0, -1);
            return false;
        }
    }
}
