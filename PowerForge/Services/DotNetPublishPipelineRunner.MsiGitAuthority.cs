using System.Globalization;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string NormalizeMsiGitRemote(string? value)
    {
        var remote = string.IsNullOrWhiteSpace(value) ? "origin" : value!.Trim();
        if (remote.StartsWith("-", StringComparison.Ordinal)
            || remote.Any(char.IsControl)
            || remote.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Invalid MSI version Git remote '{remote}'. Use a configured remote name or credential-free URL.");
        }

        return remote;
    }

    private static string NormalizeMsiGitRefPath(string? value, string label, string? defaultValue = null)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        if (string.IsNullOrWhiteSpace(candidate))
            throw new InvalidOperationException($"{label} cannot be empty.");

        var normalized = new StringBuilder(candidate!.Length);
        var previousSeparator = false;
        foreach (var character in candidate.Trim().ToLowerInvariant())
        {
            var allowed = (character >= 'a' && character <= 'z')
                          || (character >= '0' && character <= '9')
                          || character is '-' or '_' or '.';
            if (allowed)
            {
                normalized.Append(character);
                previousSeparator = false;
                continue;
            }

            if (character == '/')
            {
                if (!previousSeparator && normalized.Length > 0)
                    normalized.Append('/');
                previousSeparator = true;
                continue;
            }

            if (!previousSeparator && normalized.Length > 0)
                normalized.Append('-');
            previousSeparator = true;
        }

        var result = normalized.ToString().Trim('/', '.', '-');
        var segments = result.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{label} '{candidate}' cannot form a safe Git ref path.");
        }

        return string.Join("/", segments);
    }

    private static MsiVersionState? ReadMsiGitTagVersionState(
        string projectRoot,
        string remote,
        string tagPrefix,
        string authorityKey)
    {
        var refPrefix = BuildMsiGitTagRefPrefix(tagPrefix, authorityKey);
        var result = RunProcessCore(
            "git",
            projectRoot,
            new[] { "ls-remote", "--refs", "--tags", remote, refPrefix + "*" },
            TimeSpan.FromSeconds(30),
            environmentVariables: null);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Unable to read shared MSI version authority '{remote}:{refPrefix}'. " +
                "Verify Git connectivity and credentials before publishing.");
        }

        MsiVersionState? latest = null;
        foreach (var line in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator < 0 || separator + 1 >= line.Length)
                continue;

            var gitRef = line.Substring(separator + 1).Trim();
            if (!gitRef.StartsWith(refPrefix, StringComparison.Ordinal))
                continue;

            var version = gitRef.Substring(refPrefix.Length);
            if (!TryParseMsiVersion(version, out var major, out var minor, out var patch))
                continue;

            var candidate = new MsiVersionState
            {
                LastPatch = patch,
                Version = $"{major}.{minor}.{patch}"
            };
            latest = SelectLatestMsiVersionState(latest, candidate);
        }

        return latest;
    }

    private static void ReserveMsiGitTagVersion(
        DotNetPublishMsiVersionPlan version,
        string context,
        string reservationOwner)
    {
        if (version.Authority != DotNetPublishMsiVersionAuthorityKind.GitTags)
            return;

        if (string.IsNullOrWhiteSpace(version.AuthorityWorkingDirectory)
            || string.IsNullOrWhiteSpace(version.AuthorityKey)
            || string.IsNullOrWhiteSpace(version.GitRemote)
            || string.IsNullOrWhiteSpace(version.GitTagPrefix))
        {
            throw new InvalidOperationException(
                $"Shared Git-tag MSI authority is incomplete for {context}.");
        }

        var workingDirectory = version.AuthorityWorkingDirectory!;
        var remote = NormalizeMsiGitRemote(version.GitRemote);
        var tagPrefix = NormalizeMsiGitRefPath(version.GitTagPrefix, "MSI version Git tag prefix", "powerforge-msi");
        var authorityKey = NormalizeMsiGitRefPath(version.AuthorityKey, "MSI version authority key");
        var targetRef = BuildMsiGitTagRefPrefix(tagPrefix, authorityKey) + version.Version;

        var head = RunRequiredGit(workingDirectory, "rev-parse", "HEAD");
        var tree = RunRequiredGit(workingDirectory, "rev-parse", "HEAD^{tree}");
        var timestamp = DateTimeOffset.UtcNow;
        var identityEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_AUTHOR_NAME"] = "PowerForge MSI Authority",
            ["GIT_AUTHOR_EMAIL"] = "powerforge-msi@invalid.local",
            ["GIT_AUTHOR_DATE"] = timestamp.ToString("o", CultureInfo.InvariantCulture),
            ["GIT_COMMITTER_NAME"] = "PowerForge MSI Authority",
            ["GIT_COMMITTER_EMAIL"] = "powerforge-msi@invalid.local",
            ["GIT_COMMITTER_DATE"] = timestamp.ToString("o", CultureInfo.InvariantCulture)
        };
        var reservationMessage =
            $"Reserve MSI {version.Version}{Environment.NewLine}{Environment.NewLine}" +
            $"authority={authorityKey}{Environment.NewLine}" +
            $"owner={reservationOwner}{Environment.NewLine}" +
            $"source={head}{Environment.NewLine}" +
            $"createdUtc={timestamp:o}{Environment.NewLine}";
        var reservationCommit = RunProcessCore(
            "git",
            workingDirectory,
            new[] { "commit-tree", tree, "-p", head, "-m", reservationMessage },
            TimeSpan.FromSeconds(30),
            identityEnvironment);
        if (reservationCommit.ExitCode != 0
            || reservationCommit.TimedOut
            || string.IsNullOrWhiteSpace(reservationCommit.StdOut))
        {
            throw new InvalidOperationException(
                $"Unable to create the shared MSI version reservation object for {context}.");
        }

        var objectId = reservationCommit.StdOut.Trim();
        var push = RunProcessCore(
            "git",
            workingDirectory,
            new[] { "push", "--porcelain", remote, $"{objectId}:{targetRef}" },
            TimeSpan.FromSeconds(60),
            environmentVariables: null);
        if (push.ExitCode != 0 || push.TimedOut)
        {
            throw new InvalidOperationException(
                $"MSI version '{version.Version}' is already reserved or the shared authority " +
                $"'{remote}:{targetRef}' rejected the reservation for {context}. Re-plan or rerun to allocate the next version.");
        }
    }

    private static string RunRequiredGit(string workingDirectory, params string[] arguments)
    {
        var result = RunProcessCore(
            "git",
            workingDirectory,
            arguments,
            TimeSpan.FromSeconds(30),
            environmentVariables: null);
        if (result.ExitCode != 0 || result.TimedOut || string.IsNullOrWhiteSpace(result.StdOut))
            throw new InvalidOperationException("Unable to resolve Git source provenance for the shared MSI version authority.");

        return result.StdOut.Trim();
    }

    private static string BuildMsiGitTagRefPrefix(string tagPrefix, string authorityKey)
        => $"refs/tags/{tagPrefix}/{authorityKey}/";

    private static string? BuildMsiVersionAuthorityReference(MsiVersionResolution version)
    {
        if (version.Authority != DotNetPublishMsiVersionAuthorityKind.GitTags
            || string.IsNullOrWhiteSpace(version.GitTagPrefix)
            || string.IsNullOrWhiteSpace(version.AuthorityKey)
            || string.IsNullOrWhiteSpace(version.Version))
        {
            return version.StatePath;
        }

        return BuildMsiGitTagRefPrefix(version.GitTagPrefix!, version.AuthorityKey!) + version.Version;
    }

    private static string? BuildMsiVersionCoordinationKey(DotNetPublishMsiVersionPlan version)
    {
        if (version.Authority != DotNetPublishMsiVersionAuthorityKind.GitTags)
            return version.StatePath;

        return $"git:{version.GitRemote}:{version.GitTagPrefix}:{version.AuthorityKey}";
    }

    private static MsiVersionState? SelectLatestMsiVersionState(params MsiVersionState?[] candidates)
    {
        MsiVersionState? latest = null;
        foreach (var candidate in candidates)
        {
            if (candidate is null)
                continue;

            if (latest is null || CompareMsiVersionStates(candidate, latest) > 0)
                latest = candidate;
        }

        return latest;
    }

    private static int CompareMsiVersionStates(MsiVersionState left, MsiVersionState right)
    {
        if (TryParseMsiVersion(left.Version, out var leftMajor, out var leftMinor, out var leftPatch)
            && TryParseMsiVersion(right.Version, out var rightMajor, out var rightMinor, out var rightPatch))
        {
            var major = leftMajor.CompareTo(rightMajor);
            if (major != 0) return major;
            var minor = leftMinor.CompareTo(rightMinor);
            if (minor != 0) return minor;
            return leftPatch.CompareTo(rightPatch);
        }

        return left.LastPatch.CompareTo(right.LastPatch);
    }

    private static void ThrowIfMsiVersionLineRegresses(
        MsiVersionState? previous,
        int major,
        int minor,
        string installerId)
    {
        if (previous is null
            || !TryParseMsiVersion(previous.Version, out var previousMajor, out var previousMinor, out _))
        {
            return;
        }

        if (major < previousMajor || (major == previousMajor && minor < previousMinor))
        {
            throw new InvalidOperationException(
                $"Installer '{installerId}' requests MSI version line '{major}.{minor}', but the authority " +
                $"already contains '{previous.Version}'. Increase Major/Minor instead of regressing the product version.");
        }
    }
}
