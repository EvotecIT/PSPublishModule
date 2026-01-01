namespace PowerForge;

/// <summary>
/// Simple façade that wires default implementations together for quick consumption
/// from PSPublishModule wrappers or the CLI without requiring a DI container.
/// </summary>
/// <remarks>
/// <para>
/// This type is intentionally lightweight and opinionated. It exists to enable easy usage from:
/// </para>
/// <list type="bullet">
/// <item><description>PowerShell wrapper cmdlets (PSPublishModule)</description></item>
/// <item><description>Console apps / one-off automation (without a DI container)</description></item>
/// </list>
/// <para>
/// For more control (custom runners, filesystem abstractions, progress reporting), use the underlying services directly.
/// </para>
/// </remarks>
/// <example>
/// <summary>Normalize a PowerShell script file</summary>
/// <code>
/// var logger = new ConsoleLogger { IsVerbose = true };
/// var forge = new PowerForgeFacade(logger);
/// var result = forge.Normalize(
///     path: @"C:\Git\MyModule\Public\Get-Something.ps1",
///     options: new NormalizationOptions(LineEnding.LF, utf8Bom: true));
/// </code>
/// </example>
/// <example>
/// <summary>Format a set of PowerShell files using PSScriptAnalyzer</summary>
/// <code>
/// var logger = new ConsoleLogger { IsVerbose = true };
/// var forge = new PowerForgeFacade(logger);
/// var results = forge.Format(new[] { @"C:\Git\MyModule\Public\Get-Something.ps1" });
/// </code>
/// </example>
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
