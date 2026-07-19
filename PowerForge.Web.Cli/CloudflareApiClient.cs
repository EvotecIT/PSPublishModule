using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareApiResponse
{
    public bool Success { get; init; }
    public HttpStatusCode StatusCode { get; init; }
    public JsonElement? Result { get; init; }
    public string? TransportError { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

internal static class CloudflareApiClient
{
    internal static CloudflareApiResponse Send(
        HttpClient httpClient,
        HttpMethod method,
        string relativeUri,
        string apiToken,
        JsonNode? body)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiToken}");
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(WebCliJson.Options), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = httpClient.Send(request);
        }
        catch (Exception ex)
        {
            return new CloudflareApiResponse
            {
                Success = false,
                TransportError = $"Cloudflare API request failed: {ex.GetType().Name}: {ex.Message}",
                ErrorMessage = $"Cloudflare API request failed: {ex.GetType().Name}: {ex.Message}"
            };
        }

        using (response)
        {
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JsonElement? result = null;
            var apiSuccess = response.IsSuccessStatusCode;
            var apiMessage = string.Empty;

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    using var document = JsonDocument.Parse(content);
                    var root = document.RootElement;
                    if (root.TryGetProperty("success", out var successElement) && successElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        apiSuccess = response.IsSuccessStatusCode && successElement.GetBoolean();
                    if (root.TryGetProperty("result", out var resultElement))
                        result = resultElement.Clone();
                    apiMessage = ReadApiMessages(root);
                }
                catch (JsonException)
                {
                    apiMessage = "Cloudflare returned a non-JSON response.";
                }
            }

            if (!apiSuccess && string.IsNullOrWhiteSpace(apiMessage))
                apiMessage = response.ReasonPhrase ?? "Cloudflare API error.";

            return new CloudflareApiResponse
            {
                Success = apiSuccess,
                StatusCode = response.StatusCode,
                Result = result,
                ErrorMessage = apiSuccess
                    ? string.Empty
                    : $"Cloudflare API request failed (HTTP {(int)response.StatusCode}): {apiMessage}"
            };
        }
    }

    private static string ReadApiMessages(JsonElement root)
    {
        var messages = new List<string>();
        foreach (var propertyName in new[] { "errors", "messages" })
        {
            if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("message", out var message))
                    messages.Add(message.GetString() ?? string.Empty);
                else if (value.ValueKind == JsonValueKind.String)
                    messages.Add(value.GetString() ?? string.Empty);
            }
        }

        return string.Join("; ", messages.Where(message => !string.IsNullOrWhiteSpace(message)));
    }
}
