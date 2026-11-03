namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Signs finalized simple-tool outputs with the same provider, policies and cancellation
    /// used by the full publish pipeline, before the caller copies or archives them.
    /// </summary>
    internal void SignToolOutput(string outputDirectory, string executablePath, DotNetPublishSignOptions sign, CancellationToken cancellationToken)
    {
        var previousToken = _cancellationToken.Value;
        _cancellationToken.Value = cancellationToken;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrySignOutput(outputDirectory, sign);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_signatureMatchesPublisher(executablePath, GetPublisherMatchOptions(sign)))
                HandlePolicy(sign.OnSignFailure, $"Tool output does not have a valid signature from the configured publisher: {executablePath}");
        }
        finally
        {
            _cancellationToken.Value = previousToken;
        }
    }
}
