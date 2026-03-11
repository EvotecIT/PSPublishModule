using System.Text.Json;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => arg is "-h" or "--help"))
        {
            PrintHelp();
            return 0;
        }

        var command = args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal)) ?? "snapshot";

        var workspaceRoot = GetOption(args, "--root") ?? ResolveWorkspaceRoot();
        var databasePath = GetOption(args, "--database") ?? ReleaseStateDatabase.GetDefaultDatabasePath();
        var outputJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
        var maxPlanRepositories = ParseIntOption(args, "--max-plan", 12);
        var maxGitHubRepositories = ParseIntOption(args, "--max-github", 15);
        var top = ParseIntOption(args, "--top", 10);
        var familySelector = GetOption(args, "--family");
        var repositorySelector = GetOption(args, "--repo");
        var actionSelector = GetOption(args, "--action");

        try
        {
            var snapshotService = new WorkspaceSnapshotService();
            var queryService = new WorkspaceSnapshotQueryService();
            var commandService = new WorkspaceCommandService();
            var gitCommandService = new WorkspaceGitCommandService();
            WorkspaceSnapshot? snapshot = null;

            if (CommandRequiresSnapshot(command, familySelector))
            {
                var persistState = ShouldPersistState(command);
                snapshot = await snapshotService.RefreshAsync(
                    workspaceRoot,
                    databasePath,
                    new WorkspaceRefreshOptions(
                        MaxPlanRepositories: maxPlanRepositories,
                        MaxGitHubRepositories: maxGitHubRepositories,
                        PersistState: persistState));
            }

            if (snapshot is not null && TryWriteCommand(command, snapshot, queryService, outputJson, top, familySelector))
            {
                return 0;
            }

            if (await TryRunQueueCommandAsync(command, snapshot, commandService, databasePath, outputJson, familySelector).ConfigureAwait(false))
            {
                return 0;
            }

            if (snapshot is not null && await TryRunGitCommandAsync(command, snapshot, gitCommandService, databasePath, outputJson, repositorySelector, actionSelector).ConfigureAwait(false))
            {
                return 0;
            }

            Console.Error.WriteLine($"Unknown command '{command}'.");
            PrintHelp();
            return 2;
        }
        catch (Exception exception)
        {
            if (outputJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new {
                    success = false,
                    error = exception.Message
                }, JsonOptions));
            }
            else
            {
                Console.Error.WriteLine(exception.Message);
            }

            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    private static bool TryWriteCommand(
        string command,
        WorkspaceSnapshot snapshot,
        WorkspaceSnapshotQueryService queryService,
        bool outputJson,
        int top,
        string? familySelector)
    {
        switch (command.ToLowerInvariant())
        {
            case "snapshot":
                if (outputJson)
                {
                    WriteSnapshotJson(snapshot);
                }
                else
                {
                    WriteSnapshotText(snapshot);
                }

                return true;
            case "inbox":
                if (outputJson)
                {
                    WriteJson(new {
                        success = true,
                        workspaceRoot = snapshot.WorkspaceRoot,
                        databasePath = snapshot.DatabasePath,
                        items = queryService.GetReleaseInbox(snapshot, top)
                    });
                }
                else
                {
                    WriteInboxText(queryService.GetReleaseInbox(snapshot, top));
                }

                return true;
            case "dashboard":
                if (outputJson)
                {
                    WriteJson(new {
                        success = true,
                        workspaceRoot = snapshot.WorkspaceRoot,
                        databasePath = snapshot.DatabasePath,
                        cards = queryService.GetDashboard(snapshot)
                    });
                }
                else
                {
                    WriteDashboardText(queryService.GetDashboard(snapshot));
                }

                return true;
            case "families":
                if (outputJson)
                {
                    WriteJson(new {
                        success = true,
                        workspaceRoot = snapshot.WorkspaceRoot,
                        databasePath = snapshot.DatabasePath,
                        items = queryService.GetFamilies(snapshot, top)
                    });
                }
                else
                {
                    WriteFamiliesText(queryService.GetFamilies(snapshot, top));
                }

                return true;
            case "family-lane":
            case "family-board":
                if (string.IsNullOrWhiteSpace(familySelector))
                {
                    throw new InvalidOperationException("The family-lane command requires --family <key-or-name>.");
                }

                var lane = queryService.FindFamilyLane(snapshot, familySelector);
                if (lane is null)
                {
                    throw new InvalidOperationException($"No family lane matched '{familySelector}'.");
                }

                if (outputJson)
                {
                    WriteJson(new {
                        success = true,
                        workspaceRoot = snapshot.WorkspaceRoot,
                        databasePath = snapshot.DatabasePath,
                        lane
                    });
                }
                else
                {
                    WriteFamilyLaneText(lane);
                }

                return true;
            default:
                return false;
        }
    }

    private static async Task<bool> TryRunQueueCommandAsync(
        string command,
        WorkspaceSnapshot? snapshot,
        WorkspaceCommandService commandService,
        string databasePath,
        bool outputJson,
        string? familySelector)
    {
        ReleaseQueueCommandResult result;
        switch (command.ToLowerInvariant())
        {
            case "queue-prepare":
                if (snapshot is null)
                {
                    throw new InvalidOperationException("Queue preparation requires a workspace snapshot.");
                }

                result = await commandService.PrepareQueueAsync(snapshot, databasePath, familySelector).ConfigureAwait(false);
                break;
            case "queue-run-next":
                result = await commandService.RunNextReadyItemAsync(databasePath).ConfigureAwait(false);
                break;
            case "queue-approve-usb":
                result = await commandService.ApproveUsbAsync(databasePath).ConfigureAwait(false);
                break;
            case "queue-retry-failed":
                if (snapshot is null && !string.IsNullOrWhiteSpace(familySelector))
                {
                    throw new InvalidOperationException("Family-scoped queue retry requires a workspace snapshot.");
                }

                result = snapshot is null
                    ? await commandService.RetryFailedAsync(databasePath).ConfigureAwait(false)
                    : await commandService.RetryFailedAsync(snapshot, databasePath, familySelector).ConfigureAwait(false);
                break;
            default:
                return false;
        }

        if (outputJson)
        {
            WriteQueueCommandJson(result);
        }
        else
        {
            WriteQueueCommandText(result);
        }

        return true;
    }

    private static async Task<bool> TryRunGitCommandAsync(
        string command,
        WorkspaceSnapshot snapshot,
        WorkspaceGitCommandService gitCommandService,
        string databasePath,
        bool outputJson,
        string? repositorySelector,
        string? actionSelector)
    {
        switch (command.ToLowerInvariant())
        {
            case "git-actions":
                if (string.IsNullOrWhiteSpace(repositorySelector))
                {
                    throw new InvalidOperationException("The git-actions command requires --repo <name-or-path>.");
                }

                var catalog = await gitCommandService.GetActionCatalogAsync(snapshot, databasePath, repositorySelector).ConfigureAwait(false);
                if (outputJson)
                {
                    WriteGitCatalogJson(catalog);
                }
                else
                {
                    WriteGitCatalogText(catalog);
                }

                return true;
            case "git-run-action":
                if (string.IsNullOrWhiteSpace(repositorySelector))
                {
                    throw new InvalidOperationException("The git-run-action command requires --repo <name-or-path>.");
                }

                if (string.IsNullOrWhiteSpace(actionSelector))
                {
                    throw new InvalidOperationException("The git-run-action command requires --action <title-or-index>.");
                }

                var result = await gitCommandService.ExecuteActionAsync(snapshot, databasePath, repositorySelector, actionSelector).ConfigureAwait(false);
                if (outputJson)
                {
                    WriteGitCommandJson(result);
                }
                else
                {
                    WriteGitCommandText(result);
                }

                return true;
            default:
                return false;
        }
    }

    private static bool ShouldPersistState(string command)
        => string.Equals(command, "snapshot", StringComparison.OrdinalIgnoreCase);

    private static bool CommandRequiresSnapshot(string command, string? familySelector)
        => command.ToLowerInvariant() switch
        {
            "snapshot" => true,
            "inbox" => true,
            "dashboard" => true,
            "families" => true,
            "family-lane" => true,
            "family-board" => true,
            "queue-prepare" => true,
            "git-actions" => true,
            "git-run-action" => true,
            "queue-retry-failed" => !string.IsNullOrWhiteSpace(familySelector),
            _ => false
        };

    private static void WriteSnapshotJson(WorkspaceSnapshot snapshot)
    {
        var latestGitQuickActionLookup = snapshot.GitQuickActionReceipts
            .GroupBy(receipt => receipt.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(receipt => receipt.ExecutedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var payload = new {
            success = true,
            workspaceRoot = snapshot.WorkspaceRoot,
            databasePath = snapshot.DatabasePath,
            buildEngine = new {
                status = snapshot.BuildEngineResolution.StatusDisplay,
                source = snapshot.BuildEngineResolution.SourceDisplay,
                version = snapshot.BuildEngineResolution.VersionDisplay,
                manifestPath = snapshot.BuildEngineResolution.ManifestPath,
                warning = snapshot.BuildEngineResolution.Warning
            },
            summary = snapshot.Summary,
            queue = snapshot.QueueSession.Summary,
            stations = new {
                signing = new {
                    snapshot.SigningStation.Headline,
                    count = snapshot.SigningStation.Items.Count
                },
                publish = new {
                    snapshot.PublishStation.Headline,
                    count = snapshot.PublishStation.Items.Count
                },
                verification = new {
                    snapshot.VerificationStation.Headline,
                    count = snapshot.VerificationStation.Items.Count
                }
            },
            releaseInbox = snapshot.ReleaseInboxItems.Select(item => new {
                item.RepositoryName,
                item.Badge,
                item.Detail,
                focus = item.FocusMode.ToString(),
                item.Priority
            }).ToArray(),
            dashboard = snapshot.DashboardCards.Select(card => new {
                card.Key,
                card.Title,
                card.CountDisplay,
                detail = card.Detail,
                focus = card.FocusMode.ToString(),
                card.PresetKey
            }).ToArray(),
            families = snapshot.RepositoryFamilies.Select(family => new {
                family.DisplayName,
                family.TotalMembers,
                family.WorktreeMembers,
                family.AttentionMembers,
                family.ReadyMembers,
                family.QueueActiveMembers,
                family.MemberSummary
            }).ToArray(),
            familyLanes = snapshot.RepositoryFamilyLanes.Select(lane => new {
                lane.DisplayName,
                lane.ReadyCount,
                lane.UsbWaitingCount,
                lane.PublishReadyCount,
                lane.VerifyReadyCount,
                lane.FailedCount,
                lane.CompletedCount
            }).ToArray(),
            portfolio = snapshot.PortfolioItems.Select(item => new {
                item.Name,
                item.RootPath,
                readiness = item.ReadinessKind.ToString(),
                reason = item.ReadinessReason,
                branch = item.Git.BranchName,
                dirty = item.Git.IsDirty,
                gitGuard = item.Git.PrimaryActionableDiagnostic?.Summary,
                gitHub = item.GitHubInbox?.Summary,
                releaseDrift = item.ReleaseDrift?.Summary,
                lastGitAction = latestGitQuickActionLookup.TryGetValue(item.RootPath, out var receipt)
                    ? new {
                        receipt.ActionTitle,
                        receipt.Succeeded,
                        receipt.Summary,
                        receipt.ExecutedAtUtc
                    }
                    : null
            }).ToArray()
        };

        WriteJson(payload);
    }

    private static void WriteSnapshotText(WorkspaceSnapshot snapshot)
    {
        var latestGitQuickActionLookup = snapshot.GitQuickActionReceipts
            .GroupBy(receipt => receipt.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(receipt => receipt.ExecutedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
        var failedGitActions = latestGitQuickActionLookup.Values.Count(receipt => !receipt.Succeeded);
        var attentionRepos = snapshot.PortfolioItems
            .Where(item => item.ReadinessKind != PowerForgeStudio.Domain.Portfolio.RepositoryReadinessKind.Ready
                || item.Git.PrimaryActionableDiagnostic is not null
                || latestGitQuickActionLookup.TryGetValue(item.RootPath, out var receipt) && !receipt.Succeeded)
            .Take(10)
            .ToArray();

        Console.WriteLine($"PowerForgeStudio snapshot");
        Console.WriteLine($"Workspace: {snapshot.WorkspaceRoot}");
        Console.WriteLine($"Database:  {snapshot.DatabasePath}");
        Console.WriteLine($"Engine:    {snapshot.BuildEngineResolution.StatusDisplay} via {snapshot.BuildEngineResolution.SourceDisplay} ({snapshot.BuildEngineResolution.VersionDisplay})");
        Console.WriteLine($"Portfolio: {snapshot.Summary.TotalRepositories} repos | ready {snapshot.Summary.ReadyRepositories} | attention {snapshot.Summary.AttentionRepositories} | blocked {snapshot.Summary.BlockedRepositories}");
        Console.WriteLine($"Queue:     total {snapshot.QueueSession.Summary.TotalItems} | build-ready {snapshot.QueueSession.Summary.BuildReadyItems} | pending {snapshot.QueueSession.Summary.PreparePendingItems} | usb {snapshot.QueueSession.Summary.WaitingApprovalItems} | blocked {snapshot.QueueSession.Summary.BlockedItems}");
        Console.WriteLine($"Stations:  sign {snapshot.SigningStation.Items.Count} | publish {snapshot.PublishStation.Items.Count} | verify {snapshot.VerificationStation.Items.Count}");
        Console.WriteLine($"Dashboard: {snapshot.DashboardCards.Count} summary card(s)");
        Console.WriteLine($"Families:  {snapshot.RepositoryFamilies.Count} real family group(s)");
        Console.WriteLine($"Lanes:     {snapshot.RepositoryFamilyLanes.Count} family lane board(s)");
        Console.WriteLine($"Inbox:     {snapshot.ReleaseInboxItems.Count} action item(s)");
        Console.WriteLine($"Git:       {failedGitActions} failed quick action(s) recorded");

        if (snapshot.DashboardCards.Count > 0)
        {
            Console.WriteLine("Dashboard:");
            foreach (var card in snapshot.DashboardCards)
            {
                Console.WriteLine($"- {card.Title}: {card.CountDisplay}");
            }
        }

        if (snapshot.ReleaseInboxItems.Count > 0)
        {
            Console.WriteLine("Release Inbox:");
            foreach (var item in snapshot.ReleaseInboxItems.Take(5))
            {
                Console.WriteLine($"- [{item.Badge}] {item.RepositoryName}: {item.Detail}");
            }
        }

        if (snapshot.RepositoryFamilies.Count > 0)
        {
            Console.WriteLine("Families:");
            foreach (var family in snapshot.RepositoryFamilies.Take(5))
            {
                Console.WriteLine($"- {family.DisplayName}: {family.TotalMembers} member(s), {family.AttentionMembers} attention, {family.QueueActiveMembers} queue-active");
            }
        }

        if (snapshot.RepositoryFamilyLanes.Count > 0)
        {
            Console.WriteLine("Family Lanes:");
            foreach (var lane in snapshot.RepositoryFamilyLanes.Take(5))
            {
                Console.WriteLine($"- {lane.DisplayName}: ready {lane.ReadyCount}, usb {lane.UsbWaitingCount}, publish {lane.PublishReadyCount}, verify {lane.VerifyReadyCount}, failed {lane.FailedCount}, completed {lane.CompletedCount}");
            }
        }

        if (attentionRepos.Length == 0)
        {
            Console.WriteLine("Attention: none");
            return;
        }

        Console.WriteLine("Attention:");
        foreach (var item in attentionRepos)
        {
            var gitAction = latestGitQuickActionLookup.TryGetValue(item.RootPath, out var receipt) && !receipt.Succeeded
                ? $" | git action: {receipt.Summary}"
                : string.Empty;
            Console.WriteLine($"- {item.Name}: {item.ReadinessReason}{gitAction}");
        }
    }

    private static void WriteInboxText(IReadOnlyList<RepositoryReleaseInboxItem> inboxItems)
    {
        Console.WriteLine("PowerForgeStudio release inbox");
        if (inboxItems.Count == 0)
        {
            Console.WriteLine("No release inbox items.");
            return;
        }

        foreach (var item in inboxItems)
        {
            Console.WriteLine($"- [{item.Badge}] {item.RepositoryName}: {item.Detail}");
        }
    }

    private static void WriteDashboardText(IReadOnlyList<PortfolioDashboardSnapshot> dashboardCards)
    {
        Console.WriteLine("PowerForgeStudio dashboard");
        foreach (var card in dashboardCards)
        {
            Console.WriteLine($"- {card.Title}: {card.CountDisplay} | {card.Detail}");
        }
    }

    private static void WriteFamiliesText(IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families)
    {
        Console.WriteLine("PowerForgeStudio families");
        if (families.Count == 0)
        {
            Console.WriteLine("No repository families were detected.");
            return;
        }

        foreach (var family in families)
        {
            Console.WriteLine($"- {family.DisplayName}: {family.TotalMembers} member(s), {family.AttentionMembers} attention, {family.QueueActiveMembers} queue-active");
        }
    }

    private static void WriteFamilyLaneText(RepositoryWorkspaceFamilyLaneSnapshot lane)
    {
        Console.WriteLine($"PowerForgeStudio family lane: {lane.DisplayName}");
        Console.WriteLine(lane.Details);
        foreach (var item in lane.Members)
        {
            Console.WriteLine($"- [{item.LaneDisplay}] {item.RepositoryName} ({item.WorkspaceDisplay}): {item.Detail}");
        }
    }

    private static void WriteJson(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    private static void WriteQueueCommandJson(ReleaseQueueCommandResult result)
    {
        WriteJson(new {
            success = true,
            result.Changed,
            result.Message,
            queue = result.QueueSession is null
                ? null
                : new {
                    sessionId = result.QueueSession.SessionId,
                    scopeKey = result.QueueSession.ScopeKey,
                    scopeDisplayName = result.QueueSession.ScopeDisplayName,
                    summary = result.QueueSession.Summary,
                    items = result.QueueSession.Items.Select(item => new {
                        item.RepositoryName,
                        stage = item.Stage.ToString(),
                        status = item.Status.ToString(),
                        item.Summary,
                        item.QueueOrder,
                        item.RootPath
                    }).ToArray()
                },
            receipts = new {
                signing = result.SigningReceipts.Count,
                publish = result.PublishReceipts.Count,
                verification = result.VerificationReceipts.Count
            }
        });
    }

    private static void WriteQueueCommandText(ReleaseQueueCommandResult result)
    {
        Console.WriteLine("PowerForgeStudio queue command");
        Console.WriteLine(result.Message);

        if (result.QueueSession is null)
        {
            Console.WriteLine("No queue session is currently available.");
            return;
        }

        var scopeLabel = string.IsNullOrWhiteSpace(result.QueueSession.ScopeDisplayName)
            ? "workspace-wide"
            : $"family-scoped for {result.QueueSession.ScopeDisplayName}";
        Console.WriteLine($"Queue: {scopeLabel}");
        Console.WriteLine($"Summary: total {result.QueueSession.Summary.TotalItems} | build-ready {result.QueueSession.Summary.BuildReadyItems} | pending {result.QueueSession.Summary.PreparePendingItems} | usb {result.QueueSession.Summary.WaitingApprovalItems} | blocked {result.QueueSession.Summary.BlockedItems} | verify-ready {result.QueueSession.Summary.VerificationReadyItems}");

        foreach (var item in result.QueueSession.Items.Take(5))
        {
            Console.WriteLine($"- {item.QueueOrder}. {item.RepositoryName}: {item.Stage} / {item.Status} | {item.Summary}");
        }

        if (result.SigningReceipts.Count > 0 || result.PublishReceipts.Count > 0 || result.VerificationReceipts.Count > 0)
        {
            Console.WriteLine($"Receipts: signing {result.SigningReceipts.Count} | publish {result.PublishReceipts.Count} | verification {result.VerificationReceipts.Count}");
        }
    }

    private static void WriteGitCatalogJson(RepositoryGitActionCatalog catalog)
    {
        WriteJson(new {
            success = true,
            repository = new {
                catalog.RepositoryName,
                catalog.RootPath,
                catalog.FamilyDisplayName
            },
            latestReceipt = catalog.LatestReceipt,
            actions = catalog.Actions.Select((action, index) => new {
                index = index + 1,
                action.Title,
                action.Summary,
                kind = action.Kind.ToString(),
                action.Payload,
                action.ExecuteLabel,
                action.IsPrimary
            }).ToArray()
        });
    }

    private static void WriteGitCatalogText(RepositoryGitActionCatalog catalog)
    {
        Console.WriteLine($"PowerForgeStudio git actions: {catalog.RepositoryName}");
        Console.WriteLine($"Path: {catalog.RootPath}");
        if (catalog.LatestReceipt is not null)
        {
            Console.WriteLine($"Last action: {catalog.LatestReceipt.ActionTitle} ({catalog.LatestReceipt.StatusDisplay})");
            Console.WriteLine($"Summary: {catalog.LatestReceipt.Summary}");
        }

        foreach (var action in catalog.Actions.Select((value, index) => (Index: index + 1, Action: value)))
        {
            Console.WriteLine($"{action.Index}. [{action.Action.KindDisplay}] {action.Action.Title} | {action.Action.Summary}");
        }
    }

    private static void WriteGitCommandJson(WorkspaceGitActionCommandResult result)
    {
        WriteJson(new {
            success = true,
            result.Changed,
            result.Message,
            repository = new {
                result.Catalog.RepositoryName,
                result.Catalog.RootPath,
                result.Catalog.FamilyDisplayName
            },
            action = result.SelectedAction is null
                ? null
                : new {
                    result.SelectedAction.Title,
                    kind = result.SelectedAction.Kind.ToString(),
                    result.SelectedAction.Payload
                },
            receipt = result.Receipt
        });
    }

    private static void WriteGitCommandText(WorkspaceGitActionCommandResult result)
    {
        Console.WriteLine($"PowerForgeStudio git action: {result.Catalog.RepositoryName}");
        Console.WriteLine(result.Message);
        if (result.SelectedAction is not null)
        {
            Console.WriteLine($"Action: {result.SelectedAction.Title} [{result.SelectedAction.KindDisplay}]");
        }

        if (result.Receipt is not null)
        {
            Console.WriteLine($"Receipt: {result.Receipt.StatusDisplay} at {result.Receipt.ExecutedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
            if (!string.IsNullOrWhiteSpace(result.Receipt.OutputTail))
            {
                Console.WriteLine("Output:");
                Console.WriteLine(result.Receipt.OutputTail);
            }

            if (!string.IsNullOrWhiteSpace(result.Receipt.ErrorTail))
            {
                Console.WriteLine("Error:");
                Console.WriteLine(result.Receipt.ErrorTail);
            }
        }
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int ParseIntOption(string[] args, string optionName, int fallback)
    {
        var value = GetOption(args, optionName);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string ResolveWorkspaceRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
        {
            return configuredRoot;
        }

        const string defaultWindowsRoot = @"C:\Support\GitHub";
        if (Directory.Exists(defaultWindowsRoot))
        {
            return defaultWindowsRoot;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  powerforgestudio snapshot [--root <path>] [--database <path>] [--max-plan <n>] [--max-github <n>] [--json]");
        Console.WriteLine("  powerforgestudio inbox [--root <path>] [--database <path>] [--top <n>] [--json]");
        Console.WriteLine("  powerforgestudio dashboard [--root <path>] [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio families [--root <path>] [--database <path>] [--top <n>] [--json]");
        Console.WriteLine("  powerforgestudio family-lane --family <key-or-name> [--root <path>] [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio git-actions --repo <name-or-path> [--root <path>] [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio git-run-action --repo <name-or-path> --action <title-or-index> [--root <path>] [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio queue-prepare [--family <key-or-name>] [--root <path>] [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio queue-run-next [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio queue-approve-usb [--database <path>] [--json]");
        Console.WriteLine("  powerforgestudio queue-retry-failed [--family <key-or-name>] [--root <path>] [--database <path>] [--json]");
    }
}
