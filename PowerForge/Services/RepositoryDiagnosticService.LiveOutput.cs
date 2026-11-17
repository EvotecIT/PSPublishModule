using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class RepositoryDiagnosticService
{
    private const int MaxFailureDetailCharacters = 2000;
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
    private static readonly Regex ProtocolLine = new(@"^PF[A-Z]+::", RegexOptions.CultureInvariant);
    private static readonly Regex InfoLine = new(@"^PF[A-Z]+::INFO::([A-Za-z0-9+/=]+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Creates a repository script request whose child output reaches the log while it runs:
    /// <c>WARNING:</c> lines become warnings, <c>PF*::INFO::</c> protocol lines (such as a one-time bootstrap download)
    /// become information, and everything else except the result protocol goes to verbose.
    /// </summary>
    internal static PowerShellRunRequest CreateLiveRequest(string scriptPath, IReadOnlyList<string> args, TimeSpan timeout,
        ILogger logger, RepositoryDiagnosticRedactor? redactor = null)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        redactor ??= new RepositoryDiagnosticRedactor();
        return new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true,
            workingDirectory: null, environmentVariables: null, executableOverride: null,
            captureOutput: true, captureError: true,
            outputLineReceived: line => RelayChildLine(line, logger, redactor, isError: false),
            errorLineReceived: line => RelayChildLine(line, logger, redactor, isError: true));
    }

    /// <summary>Creates a redactor that hides the API key and credential used by one repository operation.</summary>
    internal static RepositoryDiagnosticRedactor CreateRedactor(string? apiKey, RepositoryCredential? credential)
    {
        var encodedBasic = credential is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret));
        return new RepositoryDiagnosticRedactor(apiKey, credential?.Secret, credential?.UserName, encodedBasic);
    }

    /// <summary>Short display name for a module folder, manifest or package path.</summary>
    internal static string DescribePackage(string? path)
    {
        var trimmed = (path ?? string.Empty).TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    /// <summary>Routes one child-process line: warnings to <see cref="ILogger.Warn"/>, other text to verbose.</summary>
    internal static void RelayChildLine(string? line, ILogger logger, RepositoryDiagnosticRedactor redactor, bool isError)
    {
        var text = AnsiEscape.Replace(line ?? string.Empty, string.Empty).Trim();
        if (text.Length == 0) return;
        try
        {
            var info = InfoLine.Match(text);
            if (!isError && info.Success)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(info.Groups[1].Value));
                if (!string.IsNullOrWhiteSpace(decoded)) logger.Info(redactor.Text(decoded.Trim()));
                return;
            }
            if (ProtocolLine.IsMatch(text)) return;
            if (!isError && text.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn(redactor.Text(text.Substring("WARNING:".Length).Trim()));
                return;
            }
            if (!logger.IsVerbose) return;
            if (text.StartsWith("VERBOSE:", StringComparison.OrdinalIgnoreCase))
                text = text.Substring("VERBOSE:".Length).Trim();
            logger.Verbose(redactor.Text(text));
        }
        catch
        {
            // Relaying output is best effort; the captured result still decides success.
        }
    }

    /// <summary>
    /// Builds the failure text for a child repository script: the protocol error plus any additional
    /// standard-error detail that the protocol error does not already contain.
    /// </summary>
    internal static string DescribeChildFailure(string operation, PowerShellRunResult result, string? protocolError)
    {
        var message = (protocolError ?? string.Empty).Trim();
        var stderr = AnsiEscape.Replace(result.StdErr ?? string.Empty, string.Empty).Trim();
        if (stderr.Length > 0 && message.IndexOf(stderr, StringComparison.Ordinal) < 0)
        {
            if (stderr.Length > MaxFailureDetailCharacters)
                stderr = "..." + stderr.Substring(stderr.Length - MaxFailureDetailCharacters);
            message = message.Length == 0 ? stderr : message + " Details: " + stderr;
        }
        if (message.Length == 0)
            message = result.ExitCode == 127 ? "PowerShell could not be started." : "The process reported no error text.";
        return $"{operation} failed (exit {result.ExitCode}). {message}".Trim();
    }
}
