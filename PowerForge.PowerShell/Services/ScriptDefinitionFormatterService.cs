using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge;

/// <summary>
/// Formats PowerShell script definitions using Invoke-Formatter when available.
/// </summary>
public sealed class ScriptDefinitionFormatterService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new formatter service.
    /// </summary>
    public ScriptDefinitionFormatterService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Formats the supplied script definition and falls back to the original text on failure.
    /// </summary>
    public string Format(string scriptDefinition)
    {
        if (string.IsNullOrWhiteSpace(scriptDefinition))
            return scriptDefinition;

        try
        {
            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddCommand("Invoke-Formatter")
                .AddParameter("ScriptDefinition", scriptDefinition);

            var results = ps.Invoke();
            if (ps.HadErrors)
                throw ps.Streams.Error.FirstOrDefault()?.Exception ?? new InvalidOperationException("Invoke-Formatter failed.");

            var formatted = results.FirstOrDefault()?.BaseObject?.ToString();
            return string.IsNullOrWhiteSpace(formatted) ? scriptDefinition : formatted!;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to format merge script provided by user. {ex.Message}. Using original script.");
            return scriptDefinition;
        }
    }
}
