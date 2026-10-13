using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PowerForge.Backups;

internal static class RecoverySecrets
{
    internal static Dictionary<string, string> Read(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI recovery requires Windows.");
        var secrets = new Dictionary<string, string>();
        Visit(JsonNode.Parse(File.ReadAllText(path)), "", secrets);
        return secrets;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Visit(JsonNode? node, string path, Dictionary<string, string> result)
    {
        if (node is JsonObject obj)
            foreach (var item in obj) Visit(item.Value, path + "/" + item.Key.Replace("~", "~0").Replace("/", "~1"), result);
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++) Visit(array[index], path + "/" + index, result);
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            const string prefix = "dpapi-machine:";
            if (text.StartsWith("env:", StringComparison.Ordinal))
            {
                var environment = Environment.GetEnvironmentVariable(text[4..]);
                if (string.IsNullOrEmpty(environment)) throw new InvalidDataException("A referenced recovery environment value is unavailable.");
                result.Add(path, environment);
            }
            if (text.StartsWith("dpapi-user:", StringComparison.Ordinal))
                throw new InvalidDataException("User-bound secrets require an explicit recovery identity.");
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(text[prefix.Length..]), null, DataProtectionScope.LocalMachine);
                try { result.Add(path, Encoding.UTF8.GetString(plain)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }
    }
}
