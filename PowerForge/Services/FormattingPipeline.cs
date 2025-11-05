namespace PowerForge;

/// <summary>
/// Orchestrates preprocessing (comments/empty lines), PSSA formatting, and final normalization.
/// </summary>
public sealed class FormattingPipeline
{
    private readonly ILogger _logger;
    private readonly IPowerShellRunner _runner;
    private readonly Preprocessor _pre;
    private readonly PssaFormatter _pssa;
    private readonly LineEndingsNormalizer _norm;

    /// <summary>
    /// Creates a new pipeline using the provided logger and a default runner.
    /// </summary>
    public FormattingPipeline(ILogger logger)
    {
        _logger = logger;
        _runner = new PowerShellRunner();
        _pre = new Preprocessor(_runner, logger);
        _pssa = new PssaFormatter(_runner, logger);
        _norm = new LineEndingsNormalizer();
    }

    /// <summary>
    /// Runs the pipeline for the specified files using <paramref name="options"/>.
    /// </summary>
    public IReadOnlyList<FormatterResult> Run(IEnumerable<string> files, FormatOptions options)
    {
        var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (list.Length == 0) return Array.Empty<FormatterResult>();

        // Step 1: Preprocess
        var pre = _pre.Process(list, options);

        // Step 2: PSSA
        var pssa = _pssa.FormatFilesWithSettings(list, options.PssaSettingsJson, TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

        // Step 3: Normalize
        var results = new List<FormatterResult>(list.Length);
        var opts = new NormalizationOptions(options.LineEnding, options.Utf8Bom);
        foreach (var f in list)
        {
            var n = _norm.NormalizeFile(f, opts);
            bool changed = (pre.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))?.Changed ?? false)
                           || (pssa.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))?.Changed ?? false)
                           || n.Changed;
            var msg = $"pre={(changed ? '1' : '0')}; pssa={(pssa.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))?.Changed ?? false ? '1' : '0')}; norm={(n.Changed ? '1' : '0')}";
            results.Add(new FormatterResult(f, changed, msg));
        }
        return results;
    }
}

