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
    {
        source = null;
        if (cmdlet is null)
            throw new ArgumentNullException(nameof(cmdlet));
        if (string.IsNullOrWhiteSpace(repositoryName))
            return false;

        var name = repositoryName!.Trim();
        return TryResolveWithCommand(cmdlet, "Get-PSResourceRepository", name, new[] { "Uri", "SourceLocation" }, out source) ||
               TryResolveWithCommand(cmdlet, "Get-PSRepository", name, new[] { "SourceLocation" }, out source);
    }

    private static bool TryResolveWithCommand(
        PSCmdlet cmdlet,
        string commandName,
        string repositoryName,
        string[] sourcePropertyNames,
        out string? source)
    {
        source = null;
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
                return true;
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
}
