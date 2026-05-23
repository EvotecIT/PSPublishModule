using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge;

/// <summary>
/// Lightweight persisted token storage for module documentation repository access. The legacy
/// PSMaintenance application-data path is retained so existing users keep their saved tokens.
/// DPAPI is used on Windows; elsewhere Base64 is used as a best-effort fallback.
/// </summary>
internal static class TokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string? SettingsPathOverride { get; set; }

    private static string SettingsPath
        => SettingsPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMaintenance", "settings.json");

    public static void Save(string? githubToken, string? azdoPat)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var settings = LoadSettingsObject();
        if (!settings.ContainsKey("Version")) settings["Version"] = 1;
        if (!string.IsNullOrEmpty(githubToken)) SetString(settings, "GitHub", Protect(githubToken!));
        if (!string.IsNullOrEmpty(azdoPat)) SetString(settings, "AzureDevOps", Protect(azdoPat!));

        WriteSettingsObject(settings);
    }

    public static void Clear()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;

            var settings = TryLoadSettingsObject();
            if (settings is null)
                return;

            var removed = RemoveProperty(settings, "GitHub") | RemoveProperty(settings, "AzureDevOps");
            if (removed)
                WriteSettingsObject(settings);
        }
        catch { }
    }

    public static string? GetToken(RepoHost host)
    {
        var settings = LoadSettingsObject();
        return host switch
        {
            RepoHost.GitHub => Unprotect(GetString(settings, "GitHub")),
            RepoHost.AzureDevOps => Unprotect(GetString(settings, "AzureDevOps")),
            _ => null
        };
    }

    private static JsonObject LoadSettingsObject()
        => TryLoadSettingsObject() ?? new JsonObject();

    private static JsonObject? TryLoadSettingsObject()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new JsonObject();
            var json = File.ReadAllText(SettingsPath);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch { return null; }
    }

    private static void WriteSettingsObject(JsonObject settings)
    {
        var json = settings.ToJsonString(JsonOptions);
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    private static void SetString(JsonObject settings, string propertyName, string value)
    {
        RemoveProperty(settings, propertyName);
        settings[propertyName] = value;
    }

    private static string? GetString(JsonObject settings, string propertyName)
    {
        var key = FindProperty(settings, propertyName);
        if (key is null || settings[key] is not JsonValue value)
            return null;

        return value.TryGetValue<string>(out var text) ? text : null;
    }

    private static bool RemoveProperty(JsonObject settings, string propertyName)
    {
        var key = FindProperty(settings, propertyName);
        return key is not null && settings.Remove(key);
    }

    private static string? FindProperty(JsonObject settings, string propertyName)
    {
        foreach (var property in settings)
        {
            if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Key;
        }

        return null;
    }

    private static string Protect(string value)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var prot = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(prot);
            }

            return Convert.ToBase64String(bytes);
        }
        catch { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
    }

    private static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            var data = Convert.FromBase64String(protectedValue);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var clear = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(clear);
                }
                catch
                {
                    // Backward compatibility for tokens saved by early net8 builds as base64 text.
                }
            }

            return Encoding.UTF8.GetString(data);
        }
        catch { return null; }
    }
}
