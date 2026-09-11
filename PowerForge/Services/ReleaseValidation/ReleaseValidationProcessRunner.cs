namespace PowerForge;

/// <summary>Applies one finite capture policy to every process started by release validation.</summary>
internal sealed class ReleaseValidationProcessRunner : IProcessRunner
{
    internal const int MaximumCapturedCharacters = 1024 * 1024;
    private readonly IProcessRunner _inner;

    internal ReleaseValidationProcessRunner(IProcessRunner? inner)
        => _inner = inner ?? new ProcessRunner(ownProcessTree: true);

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        request.MaxCapturedOutputCharacters = Math.Min(request.MaxCapturedOutputCharacters, MaximumCapturedCharacters);
        return _inner.RunAsync(request, cancellationToken);
    }
}
