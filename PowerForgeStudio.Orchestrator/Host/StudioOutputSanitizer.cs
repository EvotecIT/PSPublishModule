using PowerForge;

namespace PowerForgeStudio.Orchestrator.Host;

/// <summary>Bounds displayed diagnostics and applies the shared command-secret redactor.
/// Arbitrary secret values written by project code cannot be inferred by this filter.</summary>
public static class StudioOutputSanitizer
{
    /// <summary>Redacts recognized secret arguments and bounds the displayed diagnostic.</summary>
    public static string Sanitize(string? value)
    {
        var safe = DotNetPublishPipelineRunner.RedactCommandLineSecrets(value);
        return safe.Length > 4096 ? safe[..4096] : safe;
    }
}
