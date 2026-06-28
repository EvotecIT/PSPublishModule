using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Validates benchmark-delivered modules by importing them in out-of-process PowerShell hosts.
/// </summary>
public sealed class ManagedModuleImportValidationService
{
    private const string VersionMarker = "PFMMIMPORT::VERSION::";
    private readonly IPowerShellRunner _runner;
    private readonly Func<ManagedModuleImportValidationHost, string?> _executableResolver;

    /// <summary>
    /// Creates a module import validation service.
    /// </summary>
    /// <param name="runner">Optional PowerShell runner override.</param>
    public ManagedModuleImportValidationService(IPowerShellRunner? runner = null)
        : this(runner, ResolveExecutablePath)
    {
    }

    internal ManagedModuleImportValidationService(
        IPowerShellRunner? runner,
        Func<ManagedModuleImportValidationHost, string?> executableResolver)
    {
        _runner = runner ?? new PowerShellRunner();
        _executableResolver = executableResolver ?? ResolveExecutablePath;
    }

    /// <summary>
    /// Adds import validation evidence to successful benchmark runs with a module path.
    /// </summary>
    /// <param name="result">Benchmark result to enrich.</param>
    /// <param name="hosts">Optional host list. Defaults to PowerShell 7 and, on Windows, Windows PowerShell.</param>
    /// <param name="timeout">Optional timeout for each host import.</param>
    /// <returns>The same benchmark result instance with import validation evidence attached.</returns>
    public ManagedModuleBenchmarkResult Validate(
        ManagedModuleBenchmarkResult result,
        IReadOnlyList<ManagedModuleImportValidationHost>? hosts = null,
        TimeSpan? timeout = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var resolvedHosts = ResolveHosts(hosts);
        if (resolvedHosts.Count == 0)
            return result;

        foreach (var run in result.Runs ?? Array.Empty<ManagedModuleBenchmarkRunResult>())
        {
            if (!CanValidate(run))
                continue;

            run.ImportValidations = resolvedHosts
                .Select(host => ValidateRun(run, host, timeout ?? TimeSpan.FromMinutes(2)))
                .ToArray();
        }

        var existingGate = result.TransitionGates?.FirstOrDefault();
        result.TransitionGates = ManagedModuleBenchmarkTransitionGateEvaluator.Evaluate(
            result.Runs,
            existingGate?.MaximumManagedSlowdownRatio ?? 0,
            existingGate?.MaximumManagedSlowdownMilliseconds ?? 0);
        result.CompatibilityRetirement = ManagedModuleCompatibilityRetirementEvaluator.Evaluate(result);

        return result;
    }

    private ManagedModuleImportValidationResult ValidateRun(
        ManagedModuleBenchmarkRunResult run,
        ManagedModuleImportValidationHost host,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (host == ManagedModuleImportValidationHost.WindowsPowerShell && Path.DirectorySeparatorChar != '\\')
                return CreateResult(host, null, false, run.Version, null, stopwatch, "Windows PowerShell import validation is only available on Windows.");

            var executable = _executableResolver(host);
            if (string.IsNullOrWhiteSpace(executable))
                return CreateResult(host, null, false, run.Version, null, stopwatch, "Requested PowerShell validation host was not found on PATH.");

            var target = ResolveImportTarget(run);
            var command = BuildImportCommand(target, run.ModuleName);
            var powerShellResult = _runner.Run(PowerShellRunRequest.ForCommand(
                command,
                timeout,
                preferPwsh: host == ManagedModuleImportValidationHost.PowerShell7,
                executableOverride: executable,
                workingDirectory: run.ModulePath));
            var importedVersion = ParseImportedVersion(powerShellResult.StdOut);
            var succeeded = powerShellResult.ExitCode == 0 && VersionsMatch(run.Version, importedVersion);
            var message = succeeded
                ? "Imported module version matched benchmark result."
                : BuildFailureMessage(powerShellResult, importedVersion, run.Version);

            return CreateResult(host, powerShellResult.Executable, succeeded, run.Version, importedVersion, stopwatch, message);
        }
        catch (Exception ex)
        {
            return CreateResult(host, null, false, run.Version, null, stopwatch, ex.GetBaseException().Message);
        }
    }

    private static bool CanValidate(ManagedModuleBenchmarkRunResult run)
        => run.Succeeded &&
           !string.IsNullOrWhiteSpace(run.ModuleName) &&
           !string.IsNullOrWhiteSpace(run.ModulePath) &&
           Directory.Exists(run.ModulePath!);

    private static IReadOnlyList<ManagedModuleImportValidationHost> ResolveHosts(IReadOnlyList<ManagedModuleImportValidationHost>? hosts)
    {
        if (hosts is { Count: > 0 })
            return hosts.Distinct().ToArray();

        return Path.DirectorySeparatorChar == '\\'
            ? new[] { ManagedModuleImportValidationHost.PowerShell7, ManagedModuleImportValidationHost.WindowsPowerShell }
            : new[] { ManagedModuleImportValidationHost.PowerShell7 };
    }

    private static string ResolveImportTarget(ManagedModuleBenchmarkRunResult run)
    {
        var manifestPath = Path.Combine(run.ModulePath!, run.ModuleName + ".psd1");
        return File.Exists(manifestPath) ? manifestPath : run.ModulePath!;
    }

    private static string? ResolveExecutablePath(ManagedModuleImportValidationHost host)
    {
        if (host == ManagedModuleImportValidationHost.WindowsPowerShell && Path.DirectorySeparatorChar != '\\')
            return null;

        var executableName = host == ManagedModuleImportValidationHost.WindowsPowerShell
            ? "powershell.exe"
            : Path.DirectorySeparatorChar == '\\' ? "pwsh.exe" : "pwsh";
        return ResolveOnPath(executableName);
    }

    private static string? ResolveOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore inaccessible PATH entries.
            }
        }

        return null;
    }

    private static string BuildImportCommand(string target, string moduleName)
    {
        var builder = new StringBuilder();
        builder.Append("$ErrorActionPreference = 'Stop';");
        builder.Append("$target = ").Append(ToPowerShellLiteral(target)).Append(';');
        builder.Append("$moduleName = ").Append(ToPowerShellLiteral(moduleName)).Append(';');
        builder.Append("Remove-Module -Name $moduleName -Force -ErrorAction SilentlyContinue;");
        builder.Append("$module = Import-Module -Name $target -PassThru -Force -ErrorAction Stop;");
        builder.Append("$selected = @($module | Where-Object { $_.Name -eq $moduleName } | Select-Object -First 1);");
        builder.Append("if (-not $selected) { throw ('Import did not return module ' + $moduleName + '.') };");
        builder.Append("$version = [string]$selected.Version;");
        builder.Append("$pre = $null;");
        builder.Append("if ($selected.PrivateData -is [hashtable] -and $selected.PrivateData.ContainsKey('PSData')) { ");
        builder.Append("$psData = $selected.PrivateData['PSData']; ");
        builder.Append("if ($psData -is [hashtable] -and $psData.ContainsKey('Prerelease')) { $pre = [string]$psData['Prerelease'] } ");
        builder.Append("};");
        builder.Append("if (-not [string]::IsNullOrWhiteSpace($pre)) { $version = $version + '-' + $pre };");
        builder.Append("Write-Output ('").Append(VersionMarker).Append("' + $version);");
        return builder.ToString();
    }

    private static string ToPowerShellLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    private static string? ParseImportedVersion(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith(VersionMarker, StringComparison.Ordinal))
                return line.Substring(VersionMarker.Length).Trim();
        }

        return null;
    }

    private static bool VersionsMatch(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return !string.IsNullOrWhiteSpace(actual);
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            return true;

        var expectedParts = SplitVersion(expected);
        var actualParts = SplitVersion(actual);
        return string.Equals(expectedParts.Prerelease, actualParts.Prerelease, StringComparison.OrdinalIgnoreCase) &&
               NormalizeVersion(expectedParts.Version) == NormalizeVersion(actualParts.Version);
    }

    private static (string Version, string? Prerelease) SplitVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, null);

        var trimmed = value!.Trim();
        var dashIndex = trimmed.IndexOf('-');
        return dashIndex < 0
            ? (trimmed, null)
            : (trimmed.Substring(0, dashIndex), trimmed.Substring(dashIndex + 1));
    }

    private static string NormalizeVersion(string value)
    {
        var parts = value
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .ToList();
        while (parts.Count < 3)
            parts.Add(0);
        while (parts.Count > 3 && parts[parts.Count - 1] == 0)
            parts.RemoveAt(parts.Count - 1);
        return string.Join(".", parts);
    }

    private static string BuildFailureMessage(PowerShellRunResult result, string? importedVersion, string? expectedVersion)
    {
        if (result.ExitCode != 0)
            return string.IsNullOrWhiteSpace(result.StdErr)
                ? "Import validation host exited with code " + result.ExitCode + "."
                : result.StdErr.Trim();

        return "Imported version '" + (importedVersion ?? "<none>") + "' did not match expected version '" + (expectedVersion ?? "<any>") + "'.";
    }

    private static ManagedModuleImportValidationResult CreateResult(
        ManagedModuleImportValidationHost host,
        string? executable,
        bool succeeded,
        string? expectedVersion,
        string? importedVersion,
        Stopwatch stopwatch,
        string message)
    {
        stopwatch.Stop();
        return new ManagedModuleImportValidationResult
        {
            Host = host,
            HostExecutable = executable,
            Succeeded = succeeded,
            ExpectedVersion = expectedVersion,
            ImportedVersion = importedVersion,
            Elapsed = stopwatch.Elapsed,
            Message = message
        };
    }
}
