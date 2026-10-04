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
        : this(logger, new PowerShellRunner())
    {
    }

    internal FormattingPipeline(ILogger logger, IPowerShellRunner runner)
    {
        _logger = logger;
        _runner = runner;
        _pre = new Preprocessor(_runner, logger);
        _pssa = new PssaFormatter(_runner, logger);
        _norm = new LineEndingsNormalizer();
    }

    /// <summary>
    /// Runs the pipeline for the specified files using <paramref name="options"/>.
    /// </summary>
    public IReadOnlyList<FormatterResult> Run(IEnumerable<string> files, FormatOptions options)
        => RunBatches(new[] { new FormattingBatch(files, options) });

    /// <summary>Owns the isolated PSSA host across sequential formatting phases.</summary>
    internal IDisposable? BeginSession() => _pssa.BeginSession();

    internal IReadOnlyList<FormatterResult> RunBatches(IReadOnlyList<FormattingBatch> batches)
    {
        var prepared = new List<(FormattingBatch Batch, List<NormalizationResult> Normalized, IReadOnlyList<FormatterResult> Preprocessed)>();
        foreach (var batch in batches)
        {
            if (batch.Files.Length == 0) continue;
            var options = batch.Options;
            var opts = new NormalizationOptions(options.LineEnding, options.Utf8Bom);
            var preNormalize = new List<NormalizationResult>(batch.Files.Length);
            foreach (var f in batch.Files)
                preNormalize.Add(_norm.NormalizeFile(f, opts));

            var pre = options.RemoveCommentsInParamBlock || options.RemoveCommentsBeforeParamBlock ||
                      options.RemoveAllEmptyLines || options.RemoveEmptyLines
                ? _pre.Process(batch.Files, options)
                : Array.Empty<FormatterResult>();
            prepared.Add((batch, preNormalize, pre));
        }

        var pssa = _pssa.FormatBatches(prepared.Select(item => item.Batch).ToArray());

        var results = new List<FormatterResult>();
        foreach (var item in prepared)
        {
            var list = item.Batch.Files;
            var opts = new NormalizationOptions(item.Batch.Options.LineEnding, item.Batch.Options.Utf8Bom);
            var preNormalize = item.Normalized;
            var pre = item.Preprocessed;
            foreach (var f in list)
            {
                var n = _norm.NormalizeFile(f, opts);
                var preNormalizeResult = preNormalize.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase));
                var preResult = pre.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase));
                var pssaResult = pssa.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase));

                bool preNormalizeChanged = preNormalizeResult?.Changed ?? false;
                bool preChanged = preResult?.Changed ?? false;
                bool pssaChanged = pssaResult?.Changed ?? false;
                bool changed = preNormalizeChanged || preChanged || pssaChanged || n.Changed;

                var details = $"preNorm={(preNormalizeChanged ? '1' : '0')}; pre={(preChanged ? '1' : '0')}; pssa={(pssaChanged ? '1' : '0')}; norm={(n.Changed ? '1' : '0')}";

                var preMsg = preResult?.Message ?? string.Empty;
                var pssaMsg = pssaResult?.Message ?? string.Empty;

                string? statusMsg = null;
                if (FormattingSummary.IsErrorMessage(preMsg)) statusMsg = preMsg;
                else if (FormattingSummary.IsErrorMessage(pssaMsg)) statusMsg = pssaMsg;
                else if (FormattingSummary.IsSkippedMessage(preMsg)) statusMsg = preMsg;
                else if (FormattingSummary.IsSkippedMessage(pssaMsg)) statusMsg = pssaMsg;

                var msg = string.IsNullOrWhiteSpace(statusMsg) ? details : $"{statusMsg}; {details}";
                results.Add(new FormatterResult(f, changed, msg));
            }
        }
        return results;
    }
}
