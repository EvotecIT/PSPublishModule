using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
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

        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            return ImportRequiredResourceJsonFile(path);

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

    private static object ImportRequiredResourceJsonFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"RequiredResourceFile '{path}' did not contain a JSON object.");

        return ConvertJsonElement(document.RootElement)
            ?? throw new InvalidOperationException($"RequiredResourceFile '{path}' did not contain a JSON object.");
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
            "AllowClobber",
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
        var allowClobberSpecified = TryGetOption(options, "AllowClobber", out _);
        var allowClobber = GetBool(options, "AllowClobber") ?? defaults.AllowClobber;
        if (allowClobberSpecified && allowClobber && noClobber)
            throw new InvalidOperationException("RequiredResource parameters 'AllowClobber' and 'NoClobber' cannot both be true.");
        if (noClobber)
            allowClobber = false;
        var acceptLicense = GetBool(options, "AcceptLicense") ?? defaults.AcceptLicense;
        var skipDependencyCheck = GetBool(options, "SkipDependencyCheck") ?? defaults.SkipDependencyCheck;
        var scope = GetScope(options, "Scope") ?? defaults.Scope;
        var scopeSpecified = TryGetOption(options, "Scope", out _);
        var version = GetString(options, "Version");
        SplitRequiredResourceVersion(version, out var exactVersion, out var versionPolicy);
        return new ManagedModuleRequiredResourceTarget(
            name,
            exactVersion,
            minimumVersion: null,
            maximumVersion: null,
            versionPolicy,
            prerelease,
            scope,
            scopeSpecified,
            repository,
            reinstall,
            allowClobber,
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

        if (TryConvertWildcardVersionPolicy(trimmed, out var wildcardVersionPolicy))
        {
            versionPolicy = wildcardVersionPolicy;
            return;
        }

        if (ManagedModuleCommandSupport.HasWildcard(trimmed))
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

    private static bool TryConvertWildcardVersionPolicy(string value, out string? versionPolicy)
    {
        versionPolicy = null;
        if (string.Equals(value, "*", StringComparison.Ordinal))
        {
            versionPolicy = "*";
            return true;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4 ||
            !string.Equals(parts[^1], "*", StringComparison.Ordinal))
            return false;

        var specified = new int[parts.Length - 1];
        for (var i = 0; i < specified.Length; i++)
        {
            if (!int.TryParse(parts[i], out var part) || part < 0)
                return false;

            specified[i] = part;
        }

        var segmentCount = Math.Max(3, specified.Length + 1);
        var lower = new int[segmentCount];
        var upper = new int[segmentCount];
        Array.Copy(specified, lower, specified.Length);
        Array.Copy(specified, upper, specified.Length);
        upper[specified.Length - 1]++;
        versionPolicy = "[" + string.Join(".", lower) + "," + string.Join(".", upper) + ")";
        return true;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                    table[property.Name] = ConvertJsonElement(property.Value);
                return table;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ConvertJsonElement).ToArray();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind '{element.ValueKind}' in RequiredResourceFile.");
        }
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
        string? minimumVersion,
        string? maximumVersion,
        string? versionPolicy,
        bool includePrerelease,
        ManagedModuleInstallScope scope,
        bool scopeSpecified,
        string? repository,
        bool reinstall,
        bool allowClobber,
        bool acceptLicense,
        bool skipDependencyCheck)
    {
        Name = name;
        Version = version;
        MinimumVersion = minimumVersion;
        MaximumVersion = maximumVersion;
        VersionPolicy = versionPolicy;
        IncludePrerelease = includePrerelease;
        Scope = scope;
        ScopeSpecified = scopeSpecified;
        Repository = repository;
        Reinstall = reinstall;
        AllowClobber = allowClobber;
        AcceptLicense = acceptLicense;
        SkipDependencyCheck = skipDependencyCheck;
    }

    internal string Name { get; }

    internal string? Version { get; }

    internal string? MinimumVersion { get; }

    internal string? MaximumVersion { get; }

    internal string? VersionPolicy { get; }

    internal bool IncludePrerelease { get; }

    internal ManagedModuleInstallScope Scope { get; }

    internal bool ScopeSpecified { get; }

    internal string? Repository { get; }

    internal bool Reinstall { get; }

    internal bool AllowClobber { get; }

    internal bool AcceptLicense { get; }

    internal bool SkipDependencyCheck { get; }
}
