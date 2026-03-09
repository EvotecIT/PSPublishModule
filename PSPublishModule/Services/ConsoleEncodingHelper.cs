namespace PSPublishModule;

internal static class ConsoleEncodingHelper
{
    public static void TryEnableUtf8Console()
        => PowerForge.ConsoleEncoding.TryEnableUtf8Console(Spectre.Console.AnsiConsole.Profile.Capabilities.Unicode);

    public static bool ShouldRenderUnicode()
        => PowerForge.ConsoleEncoding.ShouldRenderUnicode(Spectre.Console.AnsiConsole.Profile.Capabilities.Unicode);
}
