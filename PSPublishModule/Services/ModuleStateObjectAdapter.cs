using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateObjectAdapter
{
    internal static ModuleStateDesiredState ToDesiredState(object input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var value = Unwrap(input);
        var explicitModulesValue = GetPropertyValue(value, "Modules");
        var modulesValue = explicitModulesValue ?? (IsSingleDesiredModule(value)
            ? new[] { value }
            : IsFamilyOnlyContainer(value)
                ? Array.Empty<object>()
                : value);
        var modules = ToEnumerable(modulesValue)
            .Select(ToDesiredModule)
            .ToArray();
        var familiesValue = GetPropertyValue(value, "FamilyPolicies") ?? GetPropertyValue(value, "Families");
        var families = familiesValue is null
            ? Array.Empty<ModuleStateFamilyPolicy>()
            : ToEnumerable(familiesValue).Select(ToFamilyPolicy).ToArray();

        return new ModuleStateDesiredState(modules, families);
    }

    private static ModuleStateDesiredModule ToDesiredModule(object input)
    {
        var value = Unwrap(input);
        if (value is string name)
            return new ModuleStateDesiredModule(name);

        var moduleName = GetString(value, "Name")
            ?? throw new ArgumentException("Desired module objects must include a Name property.");
        var versionPolicy =
            GetString(value, "VersionPolicy") ??
            GetString(value, "Version") ??
            GetString(value, "RequiredVersion");
        var sources =
            GetStringArray(value, "AllowedSources") ??
            GetStringArray(value, "Repositories") ??
            GetStringArray(value, "Repository");
        var scope = GetString(value, "Scope");
        var targetPath =
            GetString(value, "TargetPath") ??
            GetString(value, "Path") ??
            GetString(value, "ModuleRoot") ??
            GetString(value, "DestinationPath");
        var expectedPackageSha256 =
            GetString(value, "ExpectedPackageSha256") ??
            GetString(value, "PackageSha256") ??
            GetString(value, "Sha256");

        return new ModuleStateDesiredModule(
            moduleName,
            versionPolicy,
            sources,
            scope,
            targetPath,
            expectedPackageSha256,
            GetBool(value, "Prerelease") || GetBool(value, "IncludePrerelease"),
            GetBool(value, "Reinstall") || GetBool(value, "Force"),
            GetBool(value, "AcceptLicense"),
            GetBool(value, "AllowClobber"),
            GetBool(value, "SkipDependencyCheck"));
    }

    private static ModuleStateFamilyPolicy ToFamilyPolicy(object input)
    {
        var value = Unwrap(input);
        var name = GetString(value, "Name")
            ?? throw new ArgumentException("Family policy objects must include a Name property.");
        var modules = GetStringArray(value, "Modules") ?? Array.Empty<string>();
        var ruleText = GetString(value, "CoherenceRule");
        var rule = string.IsNullOrWhiteSpace(ruleText)
            ? ModuleStateFamilyCoherenceRule.SameVersion
            : ParseEnum<ModuleStateFamilyCoherenceRule>(ruleText!, "CoherenceRule");

        return new ModuleStateFamilyPolicy(name, modules, rule);
    }

    private static object Unwrap(object value)
        => value is PSObject psObject ? psObject.BaseObject is PSCustomObject ? psObject : psObject.BaseObject : value;

    private static bool IsSingleDesiredModule(object value)
        => GetPropertyValue(value, "Name") is not null;

    private static bool IsFamilyOnlyContainer(object value)
        => GetPropertyValue(value, "FamilyPolicies") is not null ||
           GetPropertyValue(value, "Families") is not null;

    private static IEnumerable<object> ToEnumerable(object value)
    {
        value = Unwrap(value);
        if (value is string)
            return new[] { value };
        if (value is IDictionary)
            return new[] { value };
        if (value is IEnumerable enumerable)
            return enumerable.Cast<object>().Select(Unwrap).Where(static item => item is not null);

        return new[] { value };
    }

    private static string? GetString(object value, string propertyName)
        => ConvertToString(GetPropertyValue(value, propertyName));

    private static bool GetBool(object value, string propertyName)
    {
        var propertyValue = GetPropertyValue(value, propertyName);
        if (propertyValue is null)
            return false;

        propertyValue = Unwrap(propertyValue);
        if (propertyValue is bool boolean)
            return boolean;
        if (bool.TryParse(ConvertToString(propertyValue), out var parsed))
            return parsed;

        throw new ArgumentException($"Desired module property '{propertyName}' must be a Boolean value.", propertyName);
    }

    private static string[]? GetStringArray(object value, string propertyName)
    {
        var propertyValue = GetPropertyValue(value, propertyName);
        if (propertyValue is null)
            return null;

        return ToEnumerable(propertyValue)
            .Select(ConvertToString)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static object? GetPropertyValue(object value, string propertyName)
    {
        value = Unwrap(value);
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not null &&
                    string.Equals(entry.Key.ToString(), propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        var psObject = PSObject.AsPSObject(value);
        var property = psObject.Properties[propertyName];
        if (property is not null)
            return property.Value;

        return value.GetType()
            .GetProperties()
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.GetValue(value);
    }

    private static string? ConvertToString(object? value)
    {
        if (value is null)
            return null;

        value = Unwrap(value);
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw new ArgumentException($"Unsupported {propertyName} value '{value}'.", propertyName);
    }
}
