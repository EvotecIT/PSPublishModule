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
        => ValidateObject(spec);

    internal static void Validate(DotNetPublishSpec spec)
        => ValidateObject(spec);

    internal static void ValidateJson(string json)
    {
        if (json is null)
            throw new ArgumentNullException(nameof(json));

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        ValidateElement(document.RootElement, "$", violations: new List<string>());
    }

    private static void ValidateObject<T>(T spec)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(spec));
        ValidateElement(document.RootElement, "$", violations: new List<string>());
    }

    private static void ValidateElement(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (ContainsSecretLiteral(element))
                    violations.Add(path + ".Value");

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = path + "." + property.Name;
                    bool sensitiveEnvironmentVariable = IsEnvironmentDictionaryPath(path) &&
                                                        IsSensitiveEnvironmentVariableName(property.Name);
                    if (InlineSecretPropertyNames.Contains(property.Name) &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        violations.Add(propertyPath);
                    }
                    else if (sensitiveEnvironmentVariable && TryGetInlineEnvironmentValuePath(property, propertyPath, out string? valuePath))
                    {
                        violations.Add(valuePath!);
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

    private static bool ContainsSecretLiteral(JsonElement element)
    {
        bool secret = element.EnumerateObject().Any(property =>
            property.Name.Equals("Secret", StringComparison.OrdinalIgnoreCase) &&
            property.Value.ValueKind == JsonValueKind.True);
        bool literalValue = element.EnumerateObject().Any(property =>
            property.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) &&
            property.Value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.Value.GetString()));
        return secret && literalValue;
    }

    private static bool IsEnvironmentDictionaryPath(string path)
        => path.EndsWith(".Environment", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".EnvironmentVariables", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetInlineEnvironmentValuePath(
        JsonProperty property,
        string propertyPath,
        out string? valuePath)
    {
        valuePath = null;
        if (property.Value.ValueKind == JsonValueKind.String)
        {
            if (string.IsNullOrWhiteSpace(property.Value.GetString()))
                return false;

            valuePath = propertyPath;
            return true;
        }

        if (property.Value.ValueKind != JsonValueKind.Object ||
            !property.Value.TryGetProperty("Value", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            return false;
        }

        valuePath = propertyPath + ".Value";
        return true;
    }

    private static bool IsSensitiveEnvironmentVariableName(string name)
    {
        string normalized = new string((name ?? string.Empty)
                .Trim()
                .Select(character => char.IsLetterOrDigit(character)
                    ? char.ToUpperInvariant(character)
                    : '_')
                .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        string[] suffixes =
        [
            "TOKEN",
            "PASSWORD",
            "SECRET",
            "API_KEY",
            "ACCESS_KEY",
            "PRIVATE_KEY",
            "CONNECTION_STRING",
            "CREDENTIAL"
        ];
        return suffixes.Any(suffix =>
            normalized.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase));
    }
}
