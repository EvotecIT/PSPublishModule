namespace PowerForge;

/// <summary>
/// Simple façade that wires default implementations together for quick consumption
/// from PSPublishModule wrappers or the CLI without requiring a DI container.
/// </summary>
public sealed class PowerForgeFacade
{
    private readonly ILogger _logger;
    private readonly ILineEndingsNormalizer _normalizer;
    private readonly IFormatter _formatter;

    /// <summary>
    /// Creates the façade with a given <paramref name="logger"/>.
    /// </summary>
    public PowerForgeFacade(ILogger logger)
    {
        _logger = logger;
        var runner = new PowerShellRunner();
        _normalizer = new LineEndingsNormalizer();
        _formatter = new PssaFormatter(runner, logger);
    }

    /// <summary>
    /// Normalizes a single file using the default normalizer.
    /// </summary>
    public NormalizationResult Normalize(string path, NormalizationOptions? options = null)
        => _normalizer.NormalizeFile(path, options);

    /// <summary>
    /// Formats a set of files using the out-of-proc PSScriptAnalyzer formatter.
    /// </summary>
    public IReadOnlyList<FormatterResult> Format(IEnumerable<string> files, TimeSpan? timeout = null)
        => _formatter.FormatFiles(files, timeout);
}
