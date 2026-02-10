using System;
using System.Text;
using Spectre.Console;

namespace PSPublishModule;

internal static class ConsoleEncodingHelper
{
    private static bool _attempted;

    /// <summary>
    /// Ensures the console output encoding is UTF-8 when we render Unicode via Spectre.Console.
    /// If the console is not interactive or the host doesn't support changing the encoding,
    /// this method is best-effort and will silently do nothing.
    /// </summary>
    public static void TryEnableUtf8Console()
    {
        if (_attempted) return;
        _attempted = true;

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

