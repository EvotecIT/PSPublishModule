using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Formats exception messages before they reach a Studio status, output, dialog, or tree row.</summary>
internal static class StudioDisplayError
{
    public static string From(Exception exception) => StudioOutputSanitizer.Sanitize(exception.Message);
}
