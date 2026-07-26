using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven dotnet publish workflow using <c>dotnet</c>.
/// </summary>
public sealed partial class DotNetPublishPipelineRunner
{
    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;
    private readonly AsyncLocal<CancellationToken> _cancellationToken = new();

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public DotNetPublishPipelineRunner(ILogger logger)
        : this(logger, new ProcessRunner())
    {
    }

    internal DotNetPublishPipelineRunner(ILogger logger, IProcessRunner processRunner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

}
