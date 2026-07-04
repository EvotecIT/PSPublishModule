using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge;

/// <summary>
/// Resolves registered PowerShell repository names to source locations from the current PowerShell host.
/// </summary>
public sealed class PowerShellRepositorySourceResolver
{
    /// <summary>
    /// Attempts to resolve a registered repository name to a source URI or local feed path.
    /// </summary>
    /// <param name="cmdlet">Cmdlet whose current runspace should be inspected.</param>
    /// <param name="repositoryName">Repository name to resolve.</param>
    /// <param name="source">Resolved source URI or path when available.</param>
    /// <returns>True when a registered repository source was found.</returns>
    public bool TryResolveSource(PSCmdlet cmdlet, string? repositoryName, out string? source)
        => TryResolveSource(cmdlet, repositoryName, out source, out _);

    /// <summary>
    /// Attempts to resolve a registered repository name to a source URI or local feed path and trust state.
    /// </summary>
    /// <param name="cmdlet">Cmdlet whose current runspace should be inspected.</param>
    /// <param name="repositoryName">Repository name to resolve.</param>
    /// <param name="source">Resolved source URI or path when available.</param>
    /// <param name="trusted">Registered trust state when available.</param>
    /// <returns>True when a registered repository source was found.</returns>
    public bool TryResolveSource(PSCmdlet cmdlet, string? repositoryName, out string? source, out bool trusted)
    {
        return TryResolveRepositoryLocation(cmdlet, repositoryName, publish: false, out source, out trusted);
    }

    /// <summary>
    /// Attempts to resolve a registered repository name to a publish URI or local feed path and trust state.
    /// </summary>
    /// <param name="cmdlet">Cmdlet whose current runspace should be inspected.</param>
    /// <param name="repositoryName">Repository name to resolve.</param>
    /// <param name="source">Resolved publish URI or path when available.</param>
    /// <param name="trusted">Registered trust state when available.</param>
    /// <returns>True when a registered repository publish source was found.</returns>
    public bool TryResolvePublishSource(PSCmdlet cmdlet, string? repositoryName, out string? source, out bool trusted)
    {
        return TryResolveRepositoryLocation(cmdlet, repositoryName, publish: true, out source, out trusted);
    }

    /// <summary>
    /// Attempts to resolve a registered repository name to a script source URI or local feed path and trust state.
    /// </summary>
    /// <param name="cmdlet">Cmdlet whose current runspace should be inspected.</param>
    /// <param name="repositoryName">Repository name to resolve.</param>
    /// <param name="source">Resolved script source URI or path when available.</param>
    /// <param name="trusted">Registered trust state when available.</param>
    /// <returns>True when a registered repository script source was found.</returns>
    public bool TryResolveScriptSource(PSCmdlet cmdlet, string? repositoryName, out string? source, out bool trusted)
    {
        source = null;
        trusted = false;
        if (cmdlet is null)
            throw new ArgumentNullException(nameof(cmdlet));
        if (string.IsNullOrWhiteSpace(repositoryName))
            return false;

        var name = repositoryName!.Trim();
        var resolved = TryResolveWithCommand(cmdlet, "Get-PSRepository", name, new[] { "ScriptSourceLocation" }, out source, out trusted) ||
                       TryResolveWithCommand(cmdlet, "Get-PSResourceRepository", name, new[] { "Uri", "SourceLocation" }, out source, out trusted) ||
                       TryResolveWithCommand(cmdlet, "Get-PSRepository", name, new[] { "SourceLocation" }, out source, out trusted);
        if (resolved)
            source = NormalizePowerShellGetScriptSource(source);

        return resolved;
    }

    private static bool TryResolveRepositoryLocation(PSCmdlet cmdlet, string? repositoryName, bool publish, out string? source, out bool trusted)
    {
        source = null;
        trusted = false;
        if (cmdlet is null)
            throw new ArgumentNullException(nameof(cmdlet));
        if (string.IsNullOrWhiteSpace(repositoryName))
            return false;

        var name = repositoryName!.Trim();
        return publish
            ? TryResolveWithCommand(cmdlet, "Get-PSResourceRepository", name, new[] { "PublishLocation", "PublishUri", "Uri", "SourceLocation" }, out source, out trusted) ||
              TryResolveWithCommand(cmdlet, "Get-PSRepository", name, new[] { "PublishLocation", "SourceLocation" }, out source, out trusted)
            : TryResolveWithCommand(cmdlet, "Get-PSResourceRepository", name, new[] { "Uri", "SourceLocation" }, out source, out trusted) ||
              TryResolveWithCommand(cmdlet, "Get-PSRepository", name, new[] { "SourceLocation" }, out source, out trusted);
    }

    private static bool TryResolveWithCommand(
        PSCmdlet cmdlet,
        string commandName,
        string repositoryName,
        string[] sourcePropertyNames,
        out string? source,
        out bool trusted)
    {
        source = null;
        trusted = false;
        if (cmdlet.InvokeCommand.GetCommand(commandName, CommandTypes.All) is null)
            return false;

        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand(commandName)
            .AddParameter("Name", repositoryName);

        var results = ps.Invoke();
        if (ps.HadErrors)
            return false;

        foreach (var result in results.Where(static item => item is not null))
        {
            if (!NameMatches(result, repositoryName))
                continue;

            source = ResolveFirstNonEmptyProperty(result, sourcePropertyNames);
            if (!string.IsNullOrWhiteSpace(source))
            {
                trusted = ResolveTrust(result);
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(PSObject result, string repositoryName)
    {
        var name = result.Properties["Name"]?.Value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(name) ||
               string.Equals(name, repositoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveFirstNonEmptyProperty(PSObject result, string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = result.Properties[propertyName]?.Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? NormalizePowerShellGetScriptSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        var trimmed = source!.Trim().TrimEnd('/');
        const string scriptEndpoint = "/items/psscript";
        return trimmed.EndsWith(scriptEndpoint, StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(0, trimmed.Length - scriptEndpoint.Length).TrimEnd('/')
            : trimmed;
    }

    private static bool ResolveTrust(PSObject result)
    {
        var trusted = result.Properties["Trusted"]?.Value;
        if (trusted is bool trustedValue)
            return trustedValue;
        if (bool.TryParse(trusted?.ToString(), out var parsedTrusted))
            return parsedTrusted;

        var installationPolicy = result.Properties["InstallationPolicy"]?.Value?.ToString();
        return string.Equals(installationPolicy, "Trusted", StringComparison.OrdinalIgnoreCase);
    }
}
