namespace PowerForgeStudio.Domain.Hub;

/// <summary>
/// Shared relative time formatting used across ViewModels and domain records.
/// </summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed.TotalMinutes < 1 ? "just now"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h"
            : elapsed.TotalDays < 30 ? $"{(int)elapsed.TotalDays}d"
            : elapsed.TotalDays < 365 ? $"{(int)(elapsed.TotalDays / 30)}mo"
            : $"{(int)(elapsed.TotalDays / 365)}y";
    }

    public static string FormatWithAgo(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed.TotalMinutes < 1 ? "just now"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m ago"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h ago"
            : elapsed.TotalDays < 30 ? $"{(int)elapsed.TotalDays}d ago"
            : timestamp.LocalDateTime.ToString("yyyy-MM-dd");
    }
}
