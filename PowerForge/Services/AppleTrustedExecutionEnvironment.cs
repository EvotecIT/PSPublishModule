namespace PowerForge;

/// <summary>Builds the explicit environment allowed for exact-source Apple system tools.</summary>
internal static class AppleTrustedExecutionEnvironment
{
    private static readonly string[] ForwardedOperatorVariables =
    {
        "HOME", "TMPDIR", "USER", "LOGNAME", "LANG", "LC_ALL", "SSH_AUTH_SOCK"
    };

    internal static IReadOnlyDictionary<string, string?> Create(bool isolateGitConfiguration = false)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin"
        };
        if (isolateGitConfiguration)
        {
            environment["GIT_CONFIG_NOSYSTEM"] = "1";
            environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
            environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        }

        foreach (var name in ForwardedOperatorVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                environment[name] = value;
        }
        return environment;
    }

    /// <summary>
    /// Resolves a configurable Apple tool to its fixed system path and rejects
    /// wrappers or alternate executables for provenance-bound operations.
    /// </summary>
    internal static string ResolveSystemTool(
        string? executable,
        string defaultName,
        string trustedPath,
        string operation)
    {
        var value = executable?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, defaultName, StringComparison.Ordinal) ||
            string.Equals(value, trustedPath, StringComparison.Ordinal))
        {
            return trustedPath;
        }

        throw new InvalidOperationException(
            $"{operation} requires the trusted system tool '{trustedPath}'; received '{value}'.");
    }

    /// <summary>
    /// Creates a process request for a fixed Apple system tool with an explicit
    /// allowlisted environment instead of inheriting operator build variables.
    /// </summary>
    internal static ProcessRunRequest CreateProcessRequest(
        string? executable,
        string defaultName,
        string trustedPath,
        string operation,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
        => new(
            ResolveSystemTool(executable, defaultName, trustedPath, operation),
            workingDirectory,
            arguments,
            timeout,
            Create(),
            captureOutput: true,
            captureError: true,
            inheritEnvironment: false);
}
