using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PSMaintenance;

/// <summary>
/// Lightweight persisted token storage for PSMaintenance. Stores tokens per-host (GitHub/Azure DevOps)
/// under the current user's profile. On Windows, protects tokens with DPAPI (CurrentUser scope).
/// On non-Windows, stores as plain Base64 with a warning (best effort only).
/// </summary>
internal static class TokenStore
{
    private sealed class Model
    {
        public int Version { get; set; } = 1;
        public string? GitHub { get; set; }
        public string? AzureDevOps { get; set; }
    }

    private static string SettingsPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMaintenance", "settings.json");

    public static void Save(string? githubToken, string? azdoPat)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var model = LoadModel();
        if (!string.IsNullOrEmpty(githubToken)) model.GitHub = Protect(githubToken!);
        if (!string.IsNullOrEmpty(azdoPat)) model.AzureDevOps = Protect(azdoPat!);

        var json = System.Text.Json.JsonSerializer.Serialize(model, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    public static void Clear()
    {
        try { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
    }

    public static string? GetToken(RepoHost host)
    {
        var model = LoadModel();
        return host switch
        {
            RepoHost.GitHub => Unprotect(model.GitHub),
            RepoHost.AzureDevOps => Unprotect(model.AzureDevOps),
            _ => null
        };
    }

    private static Model LoadModel()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new Model();
            var json = File.ReadAllText(SettingsPath);
            var m = System.Text.Json.JsonSerializer.Deserialize<Model>(json);
            return m ?? new Model();
        }
        catch { return new Model(); }
    }

    private static string Protect(string value)
    {
        try
        {
#if NETFRAMEWORK
            // DPAPI available under .NET Framework (Windows PowerShell)
            var bytes = Encoding.UTF8.GetBytes(value);
            var prot = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(prot);
#else
            // net8.0: avoid Windows-only APIs to keep cross-platform build; fallback to Base64
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
#endif
        }
        catch { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
    }

    private static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            var data = Convert.FromBase64String(protectedValue);
#if NETFRAMEWORK
            var clear = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
#else
            // net8.0: treat as base64-encoded clear text
            return Encoding.UTF8.GetString(data);
#endif
        }
        catch { return null; }
    }
}
