using System.Text.Json;

namespace PowerForge;

internal static class PowerForgeReleaseConfigurationSecretValidator
{
    private static readonly HashSet<string> InlineSecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AccessKey",
        "AccessToken",
        "ApiKey",
        "ApiToken",
        "AssetToken",
        "CertificatePFXBase64",
        "CertificatePFXPassword",
        "ClientSecret",
        "DemoAccountPassword",
        "GitHubAccessToken",
        "GitHubToken",
        "NugetCredentialSecret",
        "Password",
        "PfxPassword",
        "PublishApiKey",
        "Secret",
        "Token"
    };

    internal static void Validate(PowerForgeReleaseSpec spec)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(spec));
        ValidateElement(document.RootElement, "$", violations: new List<string>());
    }

    private static void ValidateElement(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = path + "." + property.Name;
                    if (InlineSecretPropertyNames.Contains(property.Name) &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        violations.Add(propertyPath);
                    }

                    ValidateElement(property.Value, propertyPath, violations);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                    ValidateElement(item, $"{path}[{index++}]", violations);
                break;
        }

        if (path == "$" && violations.Count > 0)
        {
            throw new InvalidOperationException(
                "Inline release secrets are not allowed. Use an environment-variable or file-path setting for: " +
                string.Join(", ", violations.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
        }
    }
}
