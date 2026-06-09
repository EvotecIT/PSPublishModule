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

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public DotNetPublishPipelineRunner(ILogger logger) => _logger = logger;

}
