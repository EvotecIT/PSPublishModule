namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    /// <summary>Substitutes only platform processes so release orchestration still exercises the real archive validator.</summary>
    private sealed class ArchiveIdentityProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var output = "upload-ok";
            if (request.FileName.EndsWith("plutil", StringComparison.Ordinal))
            {
                output = request.Arguments[1] switch
                {
                    "CFBundleIdentifier" => "com.example.tactra",
                    "CFBundleShortVersionString" => "1.0.0",
                    "CFBundleVersion" => "1",
                    _ => throw new InvalidOperationException("Unexpected plist key.")
                };
            }
            else
            {
                request.InvokePreStartBoundary();
                request.InvokeStartBoundary();
            }
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty, request.FileName, TimeSpan.Zero, false));
        }
    }
}
