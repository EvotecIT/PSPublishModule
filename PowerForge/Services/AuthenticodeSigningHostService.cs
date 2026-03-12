using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Shared host service for invoking <c>Register-Certificate</c> through PSPublishModule.
/// </summary>
public sealed class AuthenticodeSigningHostService
{
    private readonly IPowerShellRunner _powerShellRunner;

    /// <summary>
    /// Creates a new signing host service using the default PowerShell runner.
    /// </summary>
    public AuthenticodeSigningHostService()
        : this(new PowerShellRunner())
    {
    }

    internal AuthenticodeSigningHostService(IPowerShellRunner powerShellRunner)
    {
        _powerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
    }

    /// <summary>
    /// Executes Authenticode signing for the requested path and include patterns.
    /// </summary>
    public async Task<AuthenticodeSigningHostResult> SignAsync(AuthenticodeSigningHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequired(request.SigningPath, nameof(request.SigningPath));
        ValidateRequired(request.ModulePath, nameof(request.ModulePath));
        ValidateRequired(request.Thumbprint, nameof(request.Thumbprint));
        ValidateRequired(request.StoreName, nameof(request.StoreName));
        ValidateRequired(request.TimeStampServer, nameof(request.TimeStampServer));

        var includes = string.Join(", ", (request.IncludePatterns ?? Array.Empty<string>()).Select(QuoteLiteral));
        var script = string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(request.ModulePath),
            $"Register-Certificate -Path {QuoteLiteral(request.SigningPath)} -LocalStore {request.StoreName} -Thumbprint {QuoteLiteral(request.Thumbprint)} -TimeStampServer {QuoteLiteral(request.TimeStampServer)} -Include @({includes}) -Confirm:$false -WarningAction Stop -ErrorAction Stop | Out-Null"
        });

        var startedAt = Stopwatch.StartNew();
        var result = await Task.Run(() => _powerShellRunner.Run(PowerShellRunRequest.ForCommand(
            commandText: script,
            timeout: TimeSpan.FromMinutes(15),
            preferPwsh: !OperatingSystem.IsWindows(),
            workingDirectory: request.SigningPath,
            executableOverride: Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE"))), cancellationToken).ConfigureAwait(false);
        startedAt.Stop();

        return new AuthenticodeSigningHostResult {
            ExitCode = result.ExitCode,
            Duration = startedAt.Elapsed,
            StandardOutput = result.StdOut,
            StandardError = result.StdErr,
            Executable = result.Executable
        };
    }

    private static string BuildModuleImportClause(string modulePath)
        => File.Exists(modulePath)
            ? $"try {{ Import-Module {QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}"
            : "Import-Module PSPublishModule -Force -ErrorAction Stop";

    private static string QuoteLiteral(string value)
        => $"'{(value ?? string.Empty).Replace("'", "''")}'";

    private static void ValidateRequired(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{argumentName} is required.", argumentName);
    }
}
