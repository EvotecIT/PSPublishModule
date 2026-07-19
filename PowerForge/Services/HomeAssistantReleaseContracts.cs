using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class HomeAssistantPullRequest {
    internal int Number { get; set; }
    internal bool Merged { get; set; }
    internal string Title { get; set; } = string.Empty;
    internal string HtmlUrl { get; set; } = string.Empty;
    internal string HeadSha { get; set; } = string.Empty;
    internal string MergeCommitSha { get; set; } = string.Empty;
    internal List<string> Labels { get; } = new();
    internal List<string> ChangedFiles { get; } = new();
}

internal sealed class HomeAssistantCheckSummary {
    internal int Total { get; set; }
    internal List<string> BlockingChecks { get; } = new();
}

internal sealed class HomeAssistantWorkflowRun {
    internal long Id { get; set; }
    internal string Path { get; set; } = string.Empty;
    internal string Event { get; set; } = string.Empty;
    internal string HeadSha { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
    internal string Conclusion { get; set; } = string.Empty;
}

internal sealed class HomeAssistantGitHubRelease {
    internal long Id { get; set; }
    internal string TagName { get; set; } = string.Empty;
    internal string Body { get; set; } = string.Empty;
    internal string HtmlUrl { get; set; } = string.Empty;
    internal string TargetCommitish { get; set; } = string.Empty;
    internal bool IsDraft { get; set; }
    internal bool IsPrerelease { get; set; }
    internal List<string> AssetNames { get; } = new();
    internal Dictionary<string, long> AssetSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class HomeAssistantReleaseAssetException : InvalidOperationException {
    internal HomeAssistantReleaseAssetException(string message)
        : base(message) {
    }
}

internal sealed class HomeAssistantRepositorySnapshot {
    internal HomeAssistantRepositoryKind Kind { get; set; }
    internal string Version { get; set; } = string.Empty;
    internal string? IntegrationDirectory { get; set; }
    internal string? ManifestPath { get; set; }
    internal string? PyProjectPath { get; set; }
    internal string? PackageJsonPath { get; set; }
    internal string? PackageLockPath { get; set; }
    internal string? HacsPath { get; set; }
    internal string? HacsFileName { get; set; }
    internal bool ZipRelease { get; set; }
}

internal interface IHomeAssistantGitHubClient {
    HomeAssistantPullRequest GetPullRequest(int number);
    HomeAssistantCheckSummary GetCheckSummary(string commitSha, long? excludedWorkflowRunId);
    HomeAssistantGitHubRelease? GetLatestRelease();
    HomeAssistantGitHubRelease? FindReleaseByMarker(string marker);
    HomeAssistantGitHubRelease? GetReleaseByTag(string tagName);
    string? GetTagCommitSha(string tagName);
}

internal interface IHomeAssistantReleasePublisher {
    GitHubReleasePublishResult Publish(GitHubReleasePublishRequest request);
}

internal sealed class HomeAssistantReleasePublisher : IHomeAssistantReleasePublisher {
    private readonly GitHubReleasePublisher _publisher;

    internal HomeAssistantReleasePublisher(ILogger logger) {
        _publisher = new GitHubReleasePublisher(logger);
    }

    public GitHubReleasePublishResult Publish(GitHubReleasePublishRequest request)
        => _publisher.PublishRelease(request);
}

internal readonly struct HomeAssistantSemanticVersion : IComparable<HomeAssistantSemanticVersion> {
    internal HomeAssistantSemanticVersion(int major, int minor, int patch) {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    internal int Major { get; }
    internal int Minor { get; }
    internal int Patch { get; }

    internal static HomeAssistantSemanticVersion Parse(string value) {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("A semantic version is required.");

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(1);

        var segments = normalized.Split('.');
        if (segments.Length != 3 ||
            !int.TryParse(segments[0], out var major) ||
            !int.TryParse(segments[1], out var minor) ||
            !int.TryParse(segments[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0) {
            throw new InvalidOperationException($"Expected a three-part semantic version but found '{value}'.");
        }

        return new HomeAssistantSemanticVersion(major, minor, patch);
    }

    internal HomeAssistantSemanticVersion Increment(HomeAssistantVersionIncrement increment)
        => increment switch {
            HomeAssistantVersionIncrement.Patch => new HomeAssistantSemanticVersion(Major, Minor, checked(Patch + 1)),
            HomeAssistantVersionIncrement.Minor => new HomeAssistantSemanticVersion(Major, checked(Minor + 1), 0),
            HomeAssistantVersionIncrement.Major => new HomeAssistantSemanticVersion(checked(Major + 1), 0, 0),
            HomeAssistantVersionIncrement.None => this,
            _ => throw new ArgumentOutOfRangeException(nameof(increment), increment, "Unsupported version increment.")
        };

    public int CompareTo(HomeAssistantSemanticVersion other) {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
