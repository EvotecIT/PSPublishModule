using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareDnsRecordApplyResult
{
    public bool Success { get; init; }
    public bool DryRun { get; init; }
    public bool ChangesRequired { get; init; }
    public bool Changed { get; init; }
    public string Action { get; init; } = string.Empty;
    public string ZoneId { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string RecordType { get; init; } = string.Empty;
    public string RecordName { get; init; } = string.Empty;
    public string RecordContent { get; init; } = string.Empty;
    public bool Proxied { get; init; }
    public int Ttl { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal static class CloudflareDnsRecordManager
{
    internal static CloudflareDnsRecordApplyResult Apply(
        string zoneName,
        string apiToken,
        string recordType,
        string recordName,
        string recordContent,
        bool proxied,
        int ttl,
        string? comment,
        bool dryRun,
        WebConsoleLogger? logger,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            return Failure("Missing apiToken.", recordType, recordName, recordContent, proxied, ttl, dryRun);

        string normalizedZone;
        string normalizedType;
        string normalizedName;
        string normalizedContent;
        try
        {
            normalizedZone = NormalizeDomain(zoneName, "zoneName");
            normalizedType = NormalizeRecordType(recordType);
            normalizedName = NormalizeDomain(recordName, "recordName");
            normalizedContent = NormalizeRecordContent(normalizedType, recordContent);
            ValidateRecordScope(normalizedZone, normalizedName);
            ValidateTtl(proxied, ttl);
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message, recordType, recordName, recordContent, proxied, ttl, dryRun);
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };

        try
        {
            var zoneResponse = CloudflareApiClient.Send(
                httpClient,
                HttpMethod.Get,
                $"zones?name={Uri.EscapeDataString(normalizedZone)}&status=active&per_page=50",
                apiToken,
                body: null);
            if (!zoneResponse.Success)
                return Failure(zoneResponse.ErrorMessage, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun);
            if (!TryReadArray(zoneResponse.Result, out var zones))
                return Failure("Cloudflare zone lookup returned a malformed result.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun);
            if (zones.Count == 0)
                return Failure($"Cloudflare active zone '{normalizedZone}' was not found.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun);
            if (zones.Count != 1 || zones[0] is not JsonObject zone || !TryReadString(zone, "id", out var zoneId))
                return Failure($"Cloudflare zone lookup for '{normalizedZone}' was ambiguous or malformed.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun);

            var recordResponse = CloudflareApiClient.Send(
                httpClient,
                HttpMethod.Get,
                $"zones/{Uri.EscapeDataString(zoneId)}/dns_records?type={Uri.EscapeDataString(normalizedType)}&name={Uri.EscapeDataString(normalizedName)}&per_page=50",
                apiToken,
                body: null);
            if (!recordResponse.Success)
                return Failure(recordResponse.ErrorMessage, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId);
            if (!TryReadArray(recordResponse.Result, out var records))
                return Failure("Cloudflare DNS record lookup returned a malformed result.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId);
            if (records.Count > 1)
                return Failure($"Cloudflare returned multiple {normalizedType} records named '{normalizedName}'; refusing to choose one.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId);

            var desired = BuildDesiredRecord(normalizedType, normalizedName, normalizedContent, proxied, ttl, normalizedComment);
            if (records.Count == 0)
            {
                if (dryRun)
                {
                    var preview = $"Cloudflare DNS record {normalizedType} {normalizedName} would be created with content {normalizedContent}.";
                    logger?.Info(preview);
                    return Success("would-create", zoneId, string.Empty, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun: true, changesRequired: true, changed: false, preview);
                }

                var createResponse = CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Post,
                    $"zones/{Uri.EscapeDataString(zoneId)}/dns_records",
                    apiToken,
                    desired);
                if (!createResponse.Success)
                    return Failure(createResponse.ErrorMessage, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId);

                var createdId = TryReadResultId(createResponse.Result);
                var created = $"Created Cloudflare DNS record {normalizedType} {normalizedName} with content {normalizedContent}.";
                logger?.Info(created);
                return Success("created", zoneId, createdId, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun: false, changesRequired: true, changed: true, created);
            }

            if (records[0] is not JsonObject existing || !TryReadString(existing, "id", out var recordId))
                return Failure("Cloudflare DNS record lookup returned a malformed record.", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId);

            if (RecordMatches(existing, desired))
            {
                var current = $"Cloudflare DNS record {normalizedType} {normalizedName} is already current.";
                logger?.Info(current);
                return Success("unchanged", zoneId, recordId, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, changesRequired: false, changed: false, current);
            }

            if (dryRun)
            {
                var preview = $"Cloudflare DNS record {normalizedType} {normalizedName} would be updated to content {normalizedContent}.";
                logger?.Info(preview);
                return Success("would-update", zoneId, recordId, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun: true, changesRequired: true, changed: false, preview);
            }

            var updateResponse = CloudflareApiClient.Send(
                httpClient,
                HttpMethod.Patch,
                $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(recordId)}",
                apiToken,
                desired);
            if (!updateResponse.Success)
                return Failure(updateResponse.ErrorMessage, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun, zoneId, recordId);

            var updated = $"Updated Cloudflare DNS record {normalizedType} {normalizedName} to content {normalizedContent}.";
            logger?.Info(updated);
            return Success("updated", zoneId, recordId, normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun: false, changesRequired: true, changed: true, updated);
        }
        catch (Exception ex)
        {
            return Failure($"Cloudflare DNS record reconciliation failed: {ex.GetType().Name}: {ex.Message}", normalizedType, normalizedName, normalizedContent, proxied, ttl, dryRun);
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static string NormalizeDomain(string value, string parameterName)
    {
        var unicodeName = (value ?? string.Empty).Trim().TrimEnd('.');
        string normalized;
        try
        {
            normalized = new IdnMapping().GetAscii(unicodeName).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"{parameterName} must be a valid DNS name.");
        }
        if (string.IsNullOrWhiteSpace(normalized) || Uri.CheckHostName(normalized) != UriHostNameType.Dns)
            throw new ArgumentException($"{parameterName} must be a valid DNS name.");
        return normalized;
    }

    private static string NormalizeRecordType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("A" or "AAAA" or "CNAME"))
            throw new ArgumentException("recordType must be A, AAAA, or CNAME.");
        return normalized;
    }

    private static string NormalizeRecordContent(string recordType, string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (recordType == "CNAME")
            return NormalizeDomain(normalized, "recordContent");

        if (!IPAddress.TryParse(normalized, out var address))
            throw new ArgumentException($"recordContent must be a valid {recordType} address.");
        if (recordType == "A" && address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("recordContent must be a valid IPv4 address for an A record.");
        if (recordType == "AAAA" && address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("recordContent must be a valid IPv6 address for an AAAA record.");
        return address.ToString();
    }

    private static void ValidateRecordScope(string zoneName, string recordName)
    {
        if (!recordName.Equals(zoneName, StringComparison.Ordinal) &&
            !recordName.EndsWith('.' + zoneName, StringComparison.Ordinal))
            throw new ArgumentException($"recordName '{recordName}' must belong to zone '{zoneName}'.");
    }

    private static void ValidateTtl(bool proxied, int ttl)
    {
        if (ttl != 1 && (ttl < 60 || ttl > 86400))
            throw new ArgumentException("ttl must be 1 (automatic) or between 60 and 86400 seconds.");
        if (proxied && ttl != 1)
            throw new ArgumentException("ttl must be 1 (automatic) when proxied is true.");
    }

    private static JsonObject BuildDesiredRecord(
        string recordType,
        string recordName,
        string recordContent,
        bool proxied,
        int ttl,
        string? comment)
    {
        var desired = new JsonObject
        {
            ["type"] = recordType,
            ["name"] = recordName,
            ["content"] = recordContent,
            ["proxied"] = proxied,
            ["ttl"] = ttl
        };
        if (comment is not null)
            desired["comment"] = comment;
        return desired;
    }

    private static bool RecordMatches(JsonObject existing, JsonObject desired)
    {
        foreach (var propertyName in new[] { "type", "name", "content", "proxied", "ttl", "comment" })
        {
            if (desired[propertyName] is null)
                continue;
            if (!JsonNode.DeepEquals(existing[propertyName], desired[propertyName]))
                return false;
        }
        return true;
    }

    private static bool TryReadArray(JsonElement? result, out JsonArray values)
    {
        values = new JsonArray();
        if (result is null || result.Value.ValueKind != JsonValueKind.Array)
            return false;
        values = JsonNode.Parse(result.Value.GetRawText()) as JsonArray ?? new JsonArray();
        return true;
    }

    private static bool TryReadString(JsonObject value, string propertyName, out string result)
    {
        result = value[propertyName]?.GetValue<string>() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static string TryReadResultId(JsonElement? result)
    {
        if (result is null || result.Value.ValueKind != JsonValueKind.Object ||
            !result.Value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            return string.Empty;
        return id.GetString() ?? string.Empty;
    }

    private static CloudflareDnsRecordApplyResult Failure(
        string message,
        string recordType,
        string recordName,
        string recordContent,
        bool proxied,
        int ttl,
        bool dryRun,
        string zoneId = "",
        string recordId = "") => new()
    {
        Success = false,
        DryRun = dryRun,
        Action = "failed",
        ZoneId = zoneId,
        RecordId = recordId,
        RecordType = recordType ?? string.Empty,
        RecordName = recordName ?? string.Empty,
        RecordContent = recordContent ?? string.Empty,
        Proxied = proxied,
        Ttl = ttl,
        Message = message
    };

    private static CloudflareDnsRecordApplyResult Success(
        string action,
        string zoneId,
        string recordId,
        string recordType,
        string recordName,
        string recordContent,
        bool proxied,
        int ttl,
        bool dryRun,
        bool changesRequired,
        bool changed,
        string message) => new()
    {
        Success = true,
        DryRun = dryRun,
        ChangesRequired = changesRequired,
        Changed = changed,
        Action = action,
        ZoneId = zoneId,
        RecordId = recordId,
        RecordType = recordType,
        RecordName = recordName,
        RecordContent = recordContent,
        Proxied = proxied,
        Ttl = ttl,
        Message = message
    };
}
