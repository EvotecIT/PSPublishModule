using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class ModuleImportFailureFormatter
{
    internal static string BuildFailureMessage(PowerShellRunResult result, string? modulePath = null, string? validationTarget = null)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        var diagnostics = Parse(result.StdOut, result.StdErr);
        var cause = FirstNonEmpty(
            diagnostics.LoaderErrors.FirstOrDefault(),
            diagnostics.ImportError,
            diagnostics.StderrSummary,
            diagnostics.StdoutSummary);

        var sb = new StringBuilder();
        sb.Append("Import-Module failed");
        if (!string.IsNullOrWhiteSpace(validationTarget))
            sb.Append(" during ").Append(validationTarget!.Trim()).Append(" validation");
        sb.Append($" (exit {result.ExitCode}).");

        if (!string.IsNullOrWhiteSpace(cause))
            sb.AppendLine().Append("Cause: ").Append(cause);

        if (!string.IsNullOrWhiteSpace(diagnostics.PowerShellVersion) || !string.IsNullOrWhiteSpace(diagnostics.PSEdition))
        {
            sb.AppendLine().Append("PowerShell: ");
            if (!string.IsNullOrWhiteSpace(diagnostics.PowerShellVersion))
                sb.Append(diagnostics.PowerShellVersion);
            if (!string.IsNullOrWhiteSpace(diagnostics.PSEdition))
            {
                if (!string.IsNullOrWhiteSpace(diagnostics.PowerShellVersion))
                    sb.Append(' ');
                sb.Append('(').Append(diagnostics.PSEdition).Append(')');
            }
        }

        if (!string.IsNullOrWhiteSpace(modulePath))
            sb.AppendLine().Append("Manifest: ").Append(modulePath);

        if (!string.IsNullOrWhiteSpace(result.Executable))
            sb.AppendLine().Append("Executable: ").Append(result.Executable);

        if (diagnostics.PSModulePathEntries.Length > 0)
            sb.AppendLine().Append("PSModulePath: ").Append(string.Join(" | ", diagnostics.PSModulePathEntries));

        if (!string.IsNullOrWhiteSpace(diagnostics.StderrSummary) &&
            !StringEqualsNormalized(diagnostics.StderrSummary, cause))
        {
            sb.AppendLine().Append("Detail: ").Append(diagnostics.StderrSummary);
        }

        var extraLoaderErrors = diagnostics.LoaderErrors
            .Skip(string.Equals(diagnostics.LoaderErrors.FirstOrDefault(), cause, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
        if (extraLoaderErrors.Length > 0)
            sb.AppendLine().Append("Loader: ").Append(string.Join(" | ", extraLoaderErrors));

        if (string.IsNullOrWhiteSpace(cause) &&
            !string.IsNullOrWhiteSpace(result.StdOut))
        {
            sb.AppendLine().Append("StdOut: ").Append(Normalize(result.StdOut));
        }

        if (string.IsNullOrWhiteSpace(cause) &&
            !string.IsNullOrWhiteSpace(result.StdErr))
        {
            sb.AppendLine().Append("StdErr: ").Append(Normalize(result.StdErr));
        }

        return sb.ToString();
    }

    private static ImportFailureDiagnostics Parse(string? stdout, string? stderr)
    {
        string? psVersion = null;
        string? psEdition = null;
        string? errorType = null;
        string? importError = null;
        string? stdoutSummary = null;
        var psModulePathEntries = new List<string>();
        var loaderErrors = new List<string>();
        var inPsModulePath = false;

        foreach (var rawLine in SplitLines(stdout))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.Equals("PFIMPORT::PSMODULEPATH::BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                inPsModulePath = true;
                continue;
            }

            if (line.Equals("PFIMPORT::PSMODULEPATH::END", StringComparison.OrdinalIgnoreCase))
            {
                inPsModulePath = false;
                continue;
            }

            if (inPsModulePath)
            {
                foreach (var entry in line.Split(
                    new[] { ';', Path.PathSeparator },
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(static value => value.Trim())
                    .Where(static value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!psModulePathEntries.Contains(entry, StringComparer.OrdinalIgnoreCase))
                        psModulePathEntries.Add(entry);
                }

                continue;
            }

            if (line.StartsWith("PFIMPORT::PSVERSION::", StringComparison.OrdinalIgnoreCase))
            {
                psVersion = line.Substring("PFIMPORT::PSVERSION::".Length).Trim();
                continue;
            }

            if (line.StartsWith("PFIMPORT::PSEDITION::", StringComparison.OrdinalIgnoreCase))
            {
                psEdition = line.Substring("PFIMPORT::PSEDITION::".Length).Trim();
                continue;
            }

            if (line.StartsWith("PFIMPORT::ERRORTYPE::", StringComparison.OrdinalIgnoreCase))
            {
                errorType = line.Substring("PFIMPORT::ERRORTYPE::".Length).Trim();
                continue;
            }

            if (line.StartsWith("PFIMPORT::ERROR::", StringComparison.OrdinalIgnoreCase))
            {
                importError = line.Substring("PFIMPORT::ERROR::".Length).Trim();
                continue;
            }

            if (line.StartsWith("PFIMPORT::LOADERERROR::", StringComparison.OrdinalIgnoreCase))
            {
                var loaderError = line.Substring("PFIMPORT::LOADERERROR::".Length).Trim();
                if (!string.IsNullOrWhiteSpace(loaderError) &&
                    !loaderErrors.Contains(loaderError, StringComparer.OrdinalIgnoreCase))
                {
                    loaderErrors.Add(loaderError);
                }
                continue;
            }

            if (line.StartsWith("PFIMPORT::", StringComparison.OrdinalIgnoreCase))
                continue;

            stdoutSummary ??= line;
        }

        return new ImportFailureDiagnostics(
            psVersion,
            psEdition,
            errorType,
            importError,
            psModulePathEntries.ToArray(),
            loaderErrors.ToArray(),
            Normalize(stderr),
            stdoutSummary);
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        return string.Join(
            " ",
            SplitLines(text)
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool StringEqualsNormalized(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private sealed class ImportFailureDiagnostics
    {
        internal string? PowerShellVersion { get; }
        internal string? PSEdition { get; }
        internal string? ErrorType { get; }
        internal string? ImportError { get; }
        internal string[] PSModulePathEntries { get; }
        internal string[] LoaderErrors { get; }
        internal string? StderrSummary { get; }
        internal string? StdoutSummary { get; }

        internal ImportFailureDiagnostics(
            string? powerShellVersion,
            string? psEdition,
            string? errorType,
            string? importError,
            string[]? psModulePathEntries,
            string[]? loaderErrors,
            string? stderrSummary,
            string? stdoutSummary)
        {
            PowerShellVersion = powerShellVersion;
            PSEdition = psEdition;
            ErrorType = errorType;
            ImportError = importError;
            PSModulePathEntries = psModulePathEntries ?? Array.Empty<string>();
            LoaderErrors = loaderErrors ?? Array.Empty<string>();
            StderrSummary = stderrSummary;
            StdoutSummary = stdoutSummary;
        }
    }
}
