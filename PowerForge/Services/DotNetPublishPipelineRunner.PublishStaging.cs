namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>Owns only the temporary payload allocated for one publish invocation.</summary>
    private sealed class PublishStagingDirectory : IDisposable
    {
        private readonly ILogger _logger;
        private bool _disposed;

        internal PublishStagingDirectory(ILogger logger)
        {
            _logger = logger;
            DirectoryPath = Path.Combine(Path.GetTempPath(), "PowerForge.DotNetPublish", Guid.NewGuid().ToString("N"));
            // Never clear a pre-existing directory to claim ownership.
            if (Directory.Exists(DirectoryPath) || File.Exists(DirectoryPath))
                throw new IOException($"Publish staging path already exists: {DirectoryPath}");
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!Directory.Exists(DirectoryPath)) return;
                if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
                {
                    _logger.Warn($"Publish staging cleanup skipped a replaced directory: {DirectoryPath}");
                    return;
                }

                // Directory.Delete does not traverse child directory reparse points.
                // The parent and other invocations' staging are never cleanup targets.
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Cleanup must not replace an original publish/sign/copy failure.
                _logger.Warn($"Could not remove publish staging directory '{DirectoryPath}': {ex.Message}");
            }
        }
    }
}
