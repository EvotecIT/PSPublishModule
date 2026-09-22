using PowerForgeStudio.Domain.Workspace;
using System.IO;
using System.Text.Json;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed partial class WorkspaceRootCatalogService : IWorkspaceRootCatalogService, IWorkspaceExplorerStateStore, IWorkspaceProjectVisibilityStore, IWorkspacePreferenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    private readonly string _catalogPath;
    private readonly int _maxRecentRoots;

    public string ConfigurationPath => _catalogPath;

    public WorkspaceRootCatalogService(string? catalogPath = null, int maxRecentRoots = 8)
    {
        _catalogPath = string.IsNullOrWhiteSpace(catalogPath)
            ? PowerForgeStudioHostPaths.GetWorkspaceRootCatalogPath()
            : Path.GetFullPath(catalogPath);
        _maxRecentRoots = Math.Max(1, maxRecentRoots);
    }

    public WorkspaceRootCatalog Load(string fallbackWorkspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);

        var document = LoadDocument();
        return NormalizeCatalog(
            activeWorkspaceRoot: document?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: document?.RecentWorkspaceRoots,
            activeProfileId: document?.ActiveProfileId,
            profiles: document?.Profiles,
            templates: document?.Templates,
            preferences: document?.Preferences);
    }

    public WorkspaceRootCatalog SaveActive(string workspaceRoot, string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var existing = LoadDocument(strict: true);
        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: workspaceRoot,
            fallbackWorkspaceRoot: workspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: activeProfileId,
            profiles: existing?.Profiles,
            templates: existing?.Templates,
            preferences: existing?.Preferences);

        PersistCatalog(normalized);
        return normalized;
    }

    public WorkspaceRootCatalog ForgetRecentRoot(string workspaceRoot, string fallbackWorkspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);
        using var writeLock = AcquireWriteLock();

        var existing = LoadDocument(strict: true);
        var catalog = NormalizeCatalog(
            activeWorkspaceRoot: existing?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: existing?.ActiveProfileId,
            profiles: existing?.Profiles,
            templates: existing?.Templates,
            preferences: existing?.Preferences);
        var target = PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(workspaceRoot);
        if (PathComparer.Equals(target, catalog.ActiveWorkspaceRoot))
            throw new InvalidOperationException("The active workspace cannot be forgotten. Switch to another workspace first.");
        if (catalog.Profiles.Any(profile => PathComparer.Equals(target, profile.WorkspaceRoot)))
            throw new InvalidOperationException("A retained profile uses this workspace. Remove or change that profile before forgetting its root.");
        if (!catalog.RecentWorkspaceRoots.Contains(target, PathComparer)) return catalog;

        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: catalog.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: catalog.RecentWorkspaceRoots.Where(root => !PathComparer.Equals(root, target)).ToArray(),
            activeProfileId: catalog.ActiveProfileId,
            profiles: catalog.Profiles,
            templates: catalog.Templates,
            preferences: catalog.Preferences);
        PersistRecentRoots(normalized.RecentWorkspaceRoots);
        return normalized;
    }

    public WorkspaceRootCatalog SaveProfile(WorkspaceProfile profile, string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentNullException.ThrowIfNull(profile);

        var existing = LoadDocument(strict: true);
        var profiles = new List<WorkspaceProfile>();
        if (existing?.Profiles is not null)
        {
            profiles.AddRange(existing.Profiles.Where(existingProfile =>
                !string.Equals(existingProfile.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase)));
        }

        profiles.Add(profile with {
            WorkspaceRoot = PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(profile.WorkspaceRoot),
            TodayNote = string.IsNullOrWhiteSpace(profile.TodayNote) ? null : profile.TodayNote.Trim(),
            PreferredActionKinds = NormalizePreferredActionKinds(profile.PreferredActionKinds),
            PreferredStartupSearchText = string.IsNullOrWhiteSpace(profile.PreferredStartupSearchText) ? null : profile.PreferredStartupSearchText.Trim(),
            PreferredStartupFamilyKey = string.IsNullOrWhiteSpace(profile.PreferredStartupFamilyKey) ? null : profile.PreferredStartupFamilyKey.Trim(),
            PreferredStartupFamilyDisplayName = string.IsNullOrWhiteSpace(profile.PreferredStartupFamilyDisplayName) ? null : profile.PreferredStartupFamilyDisplayName.Trim(),
            LastLaunchResult = NormalizeLaunchResult(profile.LastLaunchResult),
            LaunchHistory = NormalizeLaunchHistory(profile.LaunchHistory, profile.LastLaunchResult),
            UpdatedAtUtc = profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : profile.UpdatedAtUtc
        });

        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: profile.WorkspaceRoot,
            fallbackWorkspaceRoot: profile.WorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: activeProfileId,
            profiles: profiles,
            templates: existing?.Templates,
            preferences: existing?.Preferences);

        PersistCatalog(normalized);
        return normalized;
    }

    public WorkspaceRootCatalog DeleteProfile(string profileId, string fallbackWorkspaceRoot, string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);

        var existing = LoadDocument(strict: true);
        var profiles = existing?.Profiles?
            .Where(profile => !string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var effectiveActiveProfileId = string.Equals(activeProfileId, profileId, StringComparison.OrdinalIgnoreCase)
            ? null
            : activeProfileId;

        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: existing?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: effectiveActiveProfileId,
            profiles: profiles,
            templates: existing?.Templates,
            preferences: existing?.Preferences);

        PersistCatalog(normalized);
        return normalized;
    }

    public WorkspaceRootCatalog SaveTemplate(WorkspaceProfileTemplate template, string fallbackWorkspaceRoot, string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);

        var existing = LoadDocument(strict: true);
        var templates = new List<WorkspaceProfileTemplate>();
        if (existing?.Templates is not null)
        {
            templates.AddRange(existing.Templates.Where(existingTemplate =>
                !string.Equals(existingTemplate.TemplateId, template.TemplateId, StringComparison.OrdinalIgnoreCase)));
        }

        templates.Add(NormalizeTemplate(template));

        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: existing?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: activeProfileId,
            profiles: existing?.Profiles,
            templates: templates,
            preferences: existing?.Preferences);
        PersistCatalog(normalized);
        return normalized;
    }

    public WorkspaceRootCatalog DeleteTemplate(string templateId, string fallbackWorkspaceRoot, string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);

        var existing = LoadDocument(strict: true);
        var templates = existing?.Templates?
            .Where(template => !string.Equals(template.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: existing?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: activeProfileId,
            profiles: existing?.Profiles,
            templates: templates,
            preferences: existing?.Preferences);

        PersistCatalog(normalized);
        return normalized;
    }

    public WorkspaceRootCatalog SavePreferences(
        WorkspaceStudioPreferences preferences,
        string fallbackWorkspaceRoot,
        string? activeProfileId = null)
    {
        using var writeLock = AcquireWriteLock();
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackWorkspaceRoot);

        var existing = LoadDocument(strict: true);
        var normalized = NormalizeCatalog(
            activeWorkspaceRoot: existing?.ActiveWorkspaceRoot,
            fallbackWorkspaceRoot: fallbackWorkspaceRoot,
            recentWorkspaceRoots: existing?.RecentWorkspaceRoots,
            activeProfileId: activeProfileId ?? existing?.ActiveProfileId,
            profiles: existing?.Profiles,
            templates: existing?.Templates,
            preferences: preferences);
        PersistCatalog(normalized);
        return normalized;
    }

    private WorkspaceRootCatalog NormalizeCatalog(
        string? activeWorkspaceRoot,
        string fallbackWorkspaceRoot,
        IReadOnlyList<string>? recentWorkspaceRoots,
        string? activeProfileId,
        IReadOnlyList<WorkspaceProfile>? profiles,
        IReadOnlyList<WorkspaceProfileTemplate>? templates,
        WorkspaceStudioPreferences? preferences)
    {
        var normalizedProfiles = NormalizeProfiles(profiles);
        var normalizedTemplates = NormalizeTemplates(templates);
        var activeProfile = normalizedProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase));
        var candidateRoots = new List<string>();
        var normalizedActiveRoot = activeProfile?.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(normalizedActiveRoot))
        {
            normalizedActiveRoot = string.IsNullOrWhiteSpace(activeWorkspaceRoot)
                ? null
                : PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(activeWorkspaceRoot);
        }

        AddDistinct(candidateRoots, normalizedActiveRoot);

        if (recentWorkspaceRoots is not null)
        {
            foreach (var recentWorkspaceRoot in recentWorkspaceRoots)
            {
                AddDistinct(candidateRoots, recentWorkspaceRoot);
            }
        }

        foreach (var profile in normalizedProfiles)
        {
            AddDistinct(candidateRoots, profile.WorkspaceRoot);
        }

        if (candidateRoots.Count == 0)
        {
            AddDistinct(candidateRoots, fallbackWorkspaceRoot);
        }

        var activeRoot = normalizedActiveRoot ?? candidateRoots.First();
        var recentRoots = candidateRoots
            .Take(_maxRecentRoots)
            .ToArray();

        return new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: activeRoot,
            RecentWorkspaceRoots: recentRoots,
            ActiveProfileId: activeProfile?.ProfileId,
            Profiles: normalizedProfiles,
            Templates: normalizedTemplates,
            Preferences: NormalizePreferences(preferences));
    }

    private static WorkspaceStudioPreferences NormalizePreferences(WorkspaceStudioPreferences? preferences)
    {
        preferences ??= WorkspaceStudioPreferences.Default;
        return preferences with
        {
            ActivityMaxGitHubRepositories = Math.Clamp(preferences.ActivityMaxGitHubRepositories, 0, 50),
            ActivityMaxIssuesPerRepository = Math.Clamp(preferences.ActivityMaxIssuesPerRepository, 0, 50),
            ActivityMaxEntries = Math.Clamp(preferences.ActivityMaxEntries, 20, 1000),
            ActivityGitHubTimeoutSeconds = Math.Clamp(preferences.ActivityGitHubTimeoutSeconds, 5, 300)
        };
    }

    private static IReadOnlyList<WorkspaceProfile> NormalizeProfiles(IReadOnlyList<WorkspaceProfile>? profiles)
    {
        if (profiles is null || profiles.Count == 0)
        {
            return [];
        }

        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ProfileId) && !string.IsNullOrWhiteSpace(profile.WorkspaceRoot))
            .Select(profile => profile with {
                WorkspaceRoot = PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(profile.WorkspaceRoot),
                DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProfileId : profile.DisplayName.Trim(),
                Description = string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description.Trim(),
                TodayNote = string.IsNullOrWhiteSpace(profile.TodayNote) ? null : profile.TodayNote.Trim(),
                PreferredActionKinds = NormalizePreferredActionKinds(profile.PreferredActionKinds),
                PreferredStartupSearchText = string.IsNullOrWhiteSpace(profile.PreferredStartupSearchText) ? null : profile.PreferredStartupSearchText.Trim(),
                PreferredStartupFamilyKey = string.IsNullOrWhiteSpace(profile.PreferredStartupFamilyKey) ? null : profile.PreferredStartupFamilyKey.Trim(),
                PreferredStartupFamilyDisplayName = string.IsNullOrWhiteSpace(profile.PreferredStartupFamilyDisplayName) ? null : profile.PreferredStartupFamilyDisplayName.Trim(),
                LastLaunchResult = NormalizeLaunchResult(profile.LastLaunchResult),
                LaunchHistory = NormalizeLaunchHistory(profile.LaunchHistory, profile.LastLaunchResult),
                QueueScopeKey = string.IsNullOrWhiteSpace(profile.QueueScopeKey) ? null : profile.QueueScopeKey.Trim(),
                QueueScopeDisplayName = string.IsNullOrWhiteSpace(profile.QueueScopeDisplayName) ? null : profile.QueueScopeDisplayName.Trim(),
                SavedViewId = string.IsNullOrWhiteSpace(profile.SavedViewId) ? null : profile.SavedViewId.Trim(),
                UpdatedAtUtc = profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : profile.UpdatedAtUtc
            })
            .GroupBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(profile => profile.UpdatedAtUtc).First())
            .OrderByDescending(profile => profile.UpdatedAtUtc)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WorkspaceProfileTemplate> NormalizeTemplates(IReadOnlyList<WorkspaceProfileTemplate>? templates)
    {
        if (templates is null || templates.Count == 0)
        {
            return [];
        }

        return templates
            .Select(NormalizeTemplate)
            .Where(template => !template.IsBuiltIn)
            .GroupBy(template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkspaceProfileTemplate NormalizeTemplate(WorkspaceProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return template with {
            TemplateId = string.IsNullOrWhiteSpace(template.TemplateId) ? "custom-template" : template.TemplateId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(template.DisplayName) ? template.TemplateId : template.DisplayName.Trim(),
            Summary = string.IsNullOrWhiteSpace(template.Summary) ? "Custom workspace profile template." : template.Summary.Trim(),
            Description = string.IsNullOrWhiteSpace(template.Description) ? "Custom workspace profile template" : template.Description.Trim(),
            TodayNote = string.IsNullOrWhiteSpace(template.TodayNote) ? "Open the desk and continue the release flow." : template.TodayNote.Trim(),
            PreferredActionKinds = NormalizePreferredActionKinds(template.PreferredActionKinds),
            PreferredStartupSearchText = string.IsNullOrWhiteSpace(template.PreferredStartupSearchText) ? null : template.PreferredStartupSearchText.Trim(),
            PreferredStartupFamily = string.IsNullOrWhiteSpace(template.PreferredStartupFamily) ? null : template.PreferredStartupFamily.Trim(),
            IsBuiltIn = false
        };
    }

    private static void AddDistinct(ICollection<string> roots, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return;
        }

        var normalizedRoot = PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(workspaceRoot);
        if (roots.Contains(normalizedRoot, PathComparer))
        {
            return;
        }

        roots.Add(normalizedRoot);
    }

    private static WorkspaceProfileLaunchResult? NormalizeLaunchResult(WorkspaceProfileLaunchResult? launchResult)
    {
        if (launchResult is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(launchResult.ActionTitle) || string.IsNullOrWhiteSpace(launchResult.Summary))
        {
            return null;
        }

        return launchResult with {
            ActionTitle = launchResult.ActionTitle.Trim(),
            Summary = launchResult.Summary.Trim(),
            ExecutedAtUtc = launchResult.ExecutedAtUtc == default ? DateTimeOffset.UtcNow : launchResult.ExecutedAtUtc
        };
    }

    private static IReadOnlyList<WorkspaceProfileLaunchResult> NormalizeLaunchHistory(
        IReadOnlyList<WorkspaceProfileLaunchResult>? launchHistory,
        WorkspaceProfileLaunchResult? lastLaunchResult)
    {
        var history = (launchHistory ?? [])
            .Select(NormalizeLaunchResult)
            .Where(result => result is not null)
            .Cast<WorkspaceProfileLaunchResult>()
            .OrderByDescending(result => result.ExecutedAtUtc)
            .Take(8)
            .ToList();

        var normalizedLastLaunch = NormalizeLaunchResult(lastLaunchResult);
        if (normalizedLastLaunch is not null && history.All(existing =>
                existing.ActionKind != normalizedLastLaunch.ActionKind
                || existing.ExecutedAtUtc != normalizedLastLaunch.ExecutedAtUtc
                || !string.Equals(existing.Summary, normalizedLastLaunch.Summary, StringComparison.Ordinal)))
        {
            history.Insert(0, normalizedLastLaunch);
        }

        return history
            .OrderByDescending(result => result.ExecutedAtUtc)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<WorkspaceProfileLaunchActionKind>? NormalizePreferredActionKinds(
        IReadOnlyList<WorkspaceProfileLaunchActionKind>? actionKinds)
    {
        if (actionKinds is null || actionKinds.Count == 0)
        {
            return null;
        }

        return actionKinds
            .Distinct()
            .ToArray();
    }

}
