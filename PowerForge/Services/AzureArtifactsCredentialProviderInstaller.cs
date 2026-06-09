using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Result of attempting to install the Azure Artifacts credential provider.
/// </summary>
public sealed class AzureArtifactsCredentialProviderInstallResult
{
    /// <summary>Whether the installation command completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Whether the installation changed the local machine state.</summary>
    public bool Changed { get; set; }

    /// <summary>Detected credential-provider file paths after installation.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();

    /// <summary>Detected credential-provider version after installation.</summary>
    public string? Version { get; set; }

    /// <summary>Messages emitted by the installer.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Installs the Azure Artifacts credential provider for the current user.
/// </summary>
public sealed class AzureArtifactsCredentialProviderInstaller
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new installer using the provided runner and logger.
    /// </summary>
    public AzureArtifactsCredentialProviderInstaller(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Installs the Azure Artifacts credential provider using Microsoft's official install script.
    /// </summary>
    public AzureArtifactsCredentialProviderInstallResult InstallForCurrentUser(
        bool includeNetFx = true,
        bool installNet8 = true,
        bool force = false,
        TimeSpan? timeout = null)
    {
        var script = BuildInstallScript();
        var args = new List<string>(3)
        {
            includeNetFx ? "1" : "0",
            installNet8 ? "1" : "0",
            force ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(5));
        var installResult = new AzureArtifactsCredentialProviderInstallResult
        {
            Changed = ParseChanged(result.StdOut),
            Paths = ParsePaths(result.StdOut),
            Version = ParseVersion(result.StdOut),
            Messages = ParseMessages(result.StdOut)
        };

        if (result.ExitCode != 0)
        {
            var message = ParseError(result.StdOut) ?? result.StdErr;
            var full = $"Azure Artifacts Credential Provider install failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            throw new InvalidOperationException(full);
        }

        installResult.Succeeded = true;
        return installResult;
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "azartifacts");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"azartifacts_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static bool ParseChanged(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFAZART::CHANGED::", StringComparison.Ordinal)) continue;
            var flag = line.Substring("PFAZART::CHANGED::".Length);
            return string.Equals(flag, "1", StringComparison.Ordinal);
        }

        return false;
    }

    private static string[] ParsePaths(string stdout)
    {
        return SplitLines(stdout)
            .Where(static line => line.StartsWith("PFAZART::PATH::", StringComparison.Ordinal))
            .Select(static line => Decode(line.Substring("PFAZART::PATH::".Length)))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ParseMessages(string stdout)
    {
        return SplitLines(stdout)
            .Where(static line => line.StartsWith("PFAZART::MESSAGE::", StringComparison.Ordinal))
            .Select(static line => Decode(line.Substring("PFAZART::MESSAGE::".Length)))
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ParseVersion(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFAZART::VERSION::", StringComparison.Ordinal)) continue;
            var value = Decode(line.Substring("PFAZART::VERSION::".Length));
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string? ParseError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFAZART::ERROR::", StringComparison.Ordinal)) continue;
            var value = Decode(line.Substring("PFAZART::ERROR::".Length));
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string BuildInstallScript()
    {
        return EmbeddedScripts.Load("Scripts/AzureArtifacts/Install-CredentialProvider.ps1");
    }
}
