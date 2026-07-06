using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedModuleRequiredResourceSupport
{
    internal static object ImportRequiredResourceFile(PSCmdlet cmdlet, string? requiredResourceFile)
    {
        var path = ManagedModuleCommandSupport.ResolveProviderPath(cmdlet, requiredResourceFile)
            ?? throw new InvalidOperationException("RequiredResourceFile path is required.");
        if (!File.Exists(path))
            throw new FileNotFoundException("RequiredResourceFile was not found.", path);

        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Microsoft.PowerShell.Utility\\Import-PowerShellDataFile")
            .AddParameter("LiteralPath", path);
        var output = ps.Invoke();
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString())));
        if (output.Count != 1)
            throw new InvalidOperationException($"RequiredResourceFile '{path}' did not return a single hashtable.");

        return output[0].BaseObject;
    }

    internal static IEnumerable<ManagedModuleRequiredResourceTarget> Parse(
        object? resource,
        ManagedModuleRequiredResourceDefaults defaults)
    {
        var table = AsDictionary(resource)
            ?? throw new InvalidOperationException("RequiredResource must be a hashtable whose keys are resource names and whose values are resource option hashtables.");

        foreach (DictionaryEntry entry in table)
        {
            var keyName = Convert.ToString(entry.Key)?.Trim();
            if (string.IsNullOrWhiteSpace(keyName))
                throw new InvalidOperationException("RequiredResource contains an empty resource name.");

            var options = AsDictionary(entry.Value)
                ?? throw new InvalidOperationException($"RequiredResource input with name '{keyName}' does not have a valid value; the value must be a hashtable.");
            yield return ParseEntry(keyName!, options, defaults);
        }
    }

    private static ManagedModuleRequiredResourceTarget ParseEntry(
        string fallbackName,
        IDictionary options,
        ManagedModuleRequiredResourceDefaults defaults)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Version",
            "Repository",
            "AcceptLicense",
            "Prerelease",
            "Scope",
            "Quiet",
            "Reinstall",
            "TrustRepository",
            "NoClobber",
            "SkipDependencyCheck"
        };
        foreach (DictionaryEntry option in options)
        {
            var key = Convert.ToString(option.Key)?.Trim();
            if (string.IsNullOrWhiteSpace(key) || !allowed.Contains(key!))
                throw new InvalidOperationException($"The parameter '{key}' provided is not a recognized or valid required resource parameter.");
        }

        var name = GetString(options, "Name") ?? fallbackName;
        var repository = GetString(options, "Repository");
        var prerelease = GetBool(options, "Prerelease") ?? defaults.IncludePrerelease;
        var reinstall = GetBool(options, "Reinstall") ?? defaults.Reinstall;
        var noClobber = GetBool(options, "NoClobber") ?? false;
        var acceptLicense = GetBool(options, "AcceptLicense") ?? defaults.AcceptLicense;
        var skipDependencyCheck = GetBool(options, "SkipDependencyCheck") ?? defaults.SkipDependencyCheck;
        var scope = GetScope(options, "Scope") ?? defaults.Scope;
        var version = GetString(options, "Version");
        SplitRequiredResourceVersion(version, out var exactVersion, out var versionPolicy);
        return new ManagedModuleRequiredResourceTarget(
            name,
            exactVersion,
            versionPolicy,
            prerelease,
            scope,
            repository,
            reinstall,
            defaults.AllowClobber && !noClobber,
            acceptLicense,
            skipDependencyCheck);
    }

    private static IDictionary? AsDictionary(object? value)
    {
        if (value is null)
            return null;
        if (value is PSObject psObject)
            value = psObject.BaseObject;
        return value as IDictionary;
    }

    private static string? GetString(IDictionary options, string key)
        => TryGetOption(options, key, out var value) && value is not null
            ? Convert.ToString(value)?.Trim()
            : null;

    private static bool? GetBool(IDictionary options, string key)
    {
        if (!TryGetOption(options, key, out var value) || value is null)
            return null;
        if (value is bool boolean)
            return boolean;
        if (bool.TryParse(Convert.ToString(value), out var parsed))
            return parsed;

        throw new InvalidOperationException($"RequiredResource parameter '{key}' must be a Boolean value.");
    }

    private static ManagedModuleInstallScope? GetScope(IDictionary options, string key)
    {
        var value = GetString(options, key);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<ManagedModuleInstallScope>(value, ignoreCase: true, out var scope))
            return scope;

        throw new InvalidOperationException($"RequiredResource parameter '{key}' has unsupported value '{value}'.");
    }

    private static bool TryGetOption(IDictionary options, string key, out object? value)
    {
        foreach (DictionaryEntry option in options)
        {
            if (string.Equals(Convert.ToString(option.Key), key, StringComparison.OrdinalIgnoreCase))
            {
                value = option.Value is PSObject psObject ? psObject.BaseObject : option.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void SplitRequiredResourceVersion(string? version, out string? exactVersion, out string? versionPolicy)
    {
        exactVersion = null;
        versionPolicy = null;
        if (string.IsNullOrWhiteSpace(version))
            return;

        var trimmed = version!.Trim();
        if ((trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("(", StringComparison.Ordinal)) &&
            trimmed.IndexOf(",", StringComparison.Ordinal) >= 0)
        {
            versionPolicy = trimmed;
            return;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
            trimmed.EndsWith("]", StringComparison.Ordinal) &&
            trimmed.IndexOf(",", StringComparison.Ordinal) < 0)
        {
            exactVersion = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return;
        }

        exactVersion = trimmed;
    }
}

internal readonly struct ManagedModuleRequiredResourceDefaults
{
    internal ManagedModuleRequiredResourceDefaults(
        bool includePrerelease,
        ManagedModuleInstallScope scope,
        bool reinstall,
        bool allowClobber,
        bool acceptLicense,
        bool skipDependencyCheck)
    {
        IncludePrerelease = includePrerelease;
        Scope = scope;
        Reinstall = reinstall;
        AllowClobber = allowClobber;
        AcceptLicense = acceptLicense;
        SkipDependencyCheck = skipDependencyCheck;
    }

    internal bool IncludePrerelease { get; }

    internal ManagedModuleInstallScope Scope { get; }

    internal bool Reinstall { get; }

    internal bool AllowClobber { get; }

    internal bool AcceptLicense { get; }

    internal bool SkipDependencyCheck { get; }
}

internal sealed class ManagedModuleRequiredResourceTarget
{
    internal ManagedModuleRequiredResourceTarget(
        string name,
        string? version,
        string? versionPolicy,
        bool includePrerelease,
        ManagedModuleInstallScope scope,
        string? repository,
        bool reinstall,
        bool allowClobber,
        bool acceptLicense,
        bool skipDependencyCheck)
    {
        Name = name;
        Version = version;
        VersionPolicy = versionPolicy;
        IncludePrerelease = includePrerelease;
        Scope = scope;
        Repository = repository;
        Reinstall = reinstall;
        AllowClobber = allowClobber;
        AcceptLicense = acceptLicense;
        SkipDependencyCheck = skipDependencyCheck;
    }

    internal string Name { get; }

    internal string? Version { get; }

    internal string? VersionPolicy { get; }

    internal bool IncludePrerelease { get; }

    internal ManagedModuleInstallScope Scope { get; }

    internal string? Repository { get; }

    internal bool Reinstall { get; }

    internal bool AllowClobber { get; }

    internal bool AcceptLicense { get; }

    internal bool SkipDependencyCheck { get; }
}
