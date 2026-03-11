using System.Text;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed class WorkspaceSnapshotQueryService
{
    public IReadOnlyList<RepositoryReleaseInboxItem> GetReleaseInbox(WorkspaceSnapshot snapshot, int maxItems = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.ReleaseInboxItems
            .Take(Math.Max(0, maxItems))
            .ToArray();
    }

    public IReadOnlyList<PortfolioDashboardSnapshot> GetDashboard(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.DashboardCards;
    }

    public IReadOnlyList<RepositoryWorkspaceFamilySnapshot> GetFamilies(WorkspaceSnapshot snapshot, int maxItems = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.RepositoryFamilies
            .Take(Math.Max(0, maxItems))
            .ToArray();
    }

    public RepositoryPortfolioItem? FindRepository(WorkspaceSnapshot snapshot, string selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        var normalizedSelector = Normalize(selector);
        var repositories = snapshot.PortfolioItems;

        var exactPath = repositories.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, selector, StringComparison.OrdinalIgnoreCase));
        if (exactPath is not null)
        {
            return exactPath;
        }

        var exactName = repositories.FirstOrDefault(repository =>
            string.Equals(repository.Name, selector, StringComparison.OrdinalIgnoreCase));
        if (exactName is not null)
        {
            return exactName;
        }

        var exactLeaf = repositories.FirstOrDefault(repository =>
            string.Equals(Path.GetFileName(repository.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), selector, StringComparison.OrdinalIgnoreCase));
        if (exactLeaf is not null)
        {
            return exactLeaf;
        }

        var normalizedName = repositories.FirstOrDefault(repository =>
            string.Equals(Normalize(repository.Name), normalizedSelector, StringComparison.Ordinal));
        if (normalizedName is not null)
        {
            return normalizedName;
        }

        var normalizedLeaf = repositories.FirstOrDefault(repository =>
            string.Equals(
                Normalize(Path.GetFileName(repository.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
                normalizedSelector,
                StringComparison.Ordinal));
        if (normalizedLeaf is not null)
        {
            return normalizedLeaf;
        }

        var partialMatches = repositories
            .Where(repository =>
                repository.Name.Contains(selector, StringComparison.OrdinalIgnoreCase)
                || repository.RootPath.Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return partialMatches.Length == 1
            ? partialMatches[0]
            : null;
    }

    public RepositoryWorkspaceFamilySnapshot? FindFamily(WorkspaceSnapshot snapshot, string selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        var lane = FindFamilyLane(snapshot, selector);
        if (lane is null)
        {
            return null;
        }

        return snapshot.RepositoryFamilies.FirstOrDefault(family =>
            string.Equals(family.FamilyKey, lane.FamilyKey, StringComparison.OrdinalIgnoreCase));
    }

    public RepositoryWorkspaceFamilyLaneSnapshot? FindFamilyLane(WorkspaceSnapshot snapshot, string selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        var normalizedSelector = Normalize(selector);
        var exactKey = snapshot.RepositoryFamilyLanes.FirstOrDefault(lane =>
            string.Equals(lane.FamilyKey, selector, StringComparison.OrdinalIgnoreCase));
        if (exactKey is not null)
        {
            return exactKey;
        }

        var exactDisplay = snapshot.RepositoryFamilyLanes.FirstOrDefault(lane =>
            string.Equals(lane.DisplayName, selector, StringComparison.OrdinalIgnoreCase));
        if (exactDisplay is not null)
        {
            return exactDisplay;
        }

        var normalizedDisplay = snapshot.RepositoryFamilyLanes.FirstOrDefault(lane =>
            string.Equals(Normalize(lane.DisplayName), normalizedSelector, StringComparison.Ordinal));
        if (normalizedDisplay is not null)
        {
            return normalizedDisplay;
        }

        var byMemberPath = snapshot.RepositoryFamilyLanes.FirstOrDefault(lane =>
            lane.Members.Any(member =>
                string.Equals(member.RootPath, selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(member.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Normalize(member.RepositoryName), normalizedSelector, StringComparison.Ordinal)));
        if (byMemberPath is not null)
        {
            return byMemberPath;
        }

        var partialMatches = snapshot.RepositoryFamilyLanes
            .Where(lane =>
                lane.DisplayName.Contains(selector, StringComparison.OrdinalIgnoreCase)
                || lane.Members.Any(member =>
                    member.RepositoryName.Contains(selector, StringComparison.OrdinalIgnoreCase)
                    || member.RootPath.Contains(selector, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return partialMatches.Length == 1
            ? partialMatches[0]
            : null;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
