namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// signtool writes its own errors to stderr, but the Azure Artifact Signing dlib reports through stdout,
    /// so the tail of both streams is kept.
    /// </summary>
    internal static string SigningToolFailureDetails(ProcessRunResult result)
    {
        var stderr = TailLines(result.StdErr, maxLines: 10, maxChars: 2000)?.Trim();
        var stdout = TailLines(result.StdOut, maxLines: 10, maxChars: 2000)?.Trim();
        return string.Join(
            Environment.NewLine,
            new[] { stderr, stdout }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// Like <see cref="HandlePolicy"/>, but a skipped signing failure is still reported as a warning: an unsigned
    /// file must never ship silently.
    /// </summary>
    private void HandleSigningFailure(DotNetPublishPolicyMode policy, string message)
    {
        if (policy == DotNetPublishPolicyMode.Skip)
        {
            _logger.Warn($"{message} Continuing without signing (OnSignFailure=Skip).");
            return;
        }

        HandlePolicy(policy, message);
    }

    private static string FirstArgument(IReadOnlyList<string> args)
        => args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "command";
}
