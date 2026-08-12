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
}
