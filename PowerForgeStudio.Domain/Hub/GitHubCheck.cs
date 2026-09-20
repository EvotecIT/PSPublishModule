namespace PowerForgeStudio.Domain.Hub;

/// <summary>A check run or commit status observed at a specific PR head.</summary>
public sealed record GitHubCheck(string Name, string State, string Source, string? Url)
{
    public bool IsFailure => State is "failure" or "error" or "timed_out" or "cancelled" or "action_required" or "startup_failure";
    public string Display => $"{State.Replace('_', ' ')} · {Source}";
}
