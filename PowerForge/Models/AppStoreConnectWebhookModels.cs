using System.Text.Json;

namespace PowerForge;

/// <summary>Configured App Store Connect webhook.</summary>
public sealed class AppStoreConnectWebhookInfo
{
    /// <summary>Webhook resource id.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Configured display name.</summary>
    public string? Name { get; set; }
    /// <summary>HTTPS destination URL.</summary>
    public string? Url { get; set; }
    /// <summary>Whether Apple currently sends notifications.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Configured App Store Connect event types.</summary>
    public string[] EventTypes { get; set; } = Array.Empty<string>();
}

/// <summary>Desired webhook configuration.</summary>
public sealed class AppStoreConnectWebhookSpec
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>Stable webhook name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Public HTTPS receiver URL.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Shared HMAC secret; never write it to receipts or logs.</summary>
    public string Secret { get; set; } = string.Empty;
    /// <summary>Whether the webhook is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>App Store Connect event types to receive.</summary>
    public string[] EventTypes { get; set; } = Array.Empty<string>();
}

/// <summary>Verified App Store Connect webhook notification envelope.</summary>
public sealed class AppStoreConnectWebhookNotification
{
    /// <summary>Notification event id.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Notification payload type.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Payload schema version.</summary>
    public int Version { get; set; }
    /// <summary>Event timestamp when Apple provides one.</summary>
    public DateTimeOffset? Timestamp { get; set; }
    /// <summary>Previous state when the event reports a transition.</summary>
    public string? PreviousState { get; set; }
    /// <summary>New state when the event reports a transition.</summary>
    public string? NewState { get; set; }
    /// <summary>Related App Store Connect resource type.</summary>
    public string? InstanceType { get; set; }
    /// <summary>Related App Store Connect resource id.</summary>
    public string? InstanceId { get; set; }
    /// <summary>Cloned event-specific attributes for downstream processors.</summary>
    public JsonElement Attributes { get; set; }
    /// <summary>Whether the reported state is a terminal failure.</summary>
    public bool IsFailure { get; set; }
    /// <summary>Whether the receiver should refresh the compact release state.</summary>
    public bool ShouldRefreshReleaseState { get; set; }
    /// <summary>Smallest safe follow-up actions for this event.</summary>
    public string[] NextActions { get; set; } = Array.Empty<string>();
}
