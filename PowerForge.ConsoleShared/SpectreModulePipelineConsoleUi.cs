using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static class SpectreModulePipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(
        bool isVerbose,
        bool outputJson = false,
        bool quiet = false,
        bool noColor = false,
        ConsoleView view = ConsoleView.Standard)
    {
        if (isVerbose || outputJson || quiet || noColor) return false;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected) return false;

        ConsoleEncoding.TryEnableUtf8Console(AnsiConsole.Profile.Capabilities.Unicode);

        var resolvedView = ResolveView(view);
        if (resolvedView != ConsoleView.Standard) return false;

        return !ConsoleEnvironment.IsCI && AnsiConsole.Profile.Capabilities.Interactive;
    }

    public static ModulePipelineResult RunInteractive(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configLabel)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        return RunInteractive(
            AnsiConsole.Console,
            plan,
            configLabel,
            progress => runner.Run(spec, plan, progress));
    }

    internal static ModulePipelineResult RunInteractive(
        IAnsiConsole console,
        ModulePipelinePlan plan,
        string? configLabel,
        Func<IModulePipelineProgressReporter, ModulePipelineResult> run)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var steps = ModulePipelineStep.Create(plan);
        WriteHeader(console, plan, configLabel, steps);
        var presentation = SpectreProgressPresentation.Create(console);

        Exception? failure = null;
        ModulePipelineResult? result = null;
        SpectreProgressDisplay.Run(
            console,
            presentation.CreateColumns(),
            ctx =>
            {
                var ledger = new SpectreProgressLedger(ctx, presentation);
                var items = ModulePipelineProgressItemFactory.Create(plan)
                    .Select(ToLedgerItem)
                    .ToArray();
                ledger.Plan(items);
                var reporter = new SpectrePipelineProgressReporter(
                    ledger,
                    items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase));
                try
                {
                    result = run(reporter);
                    ledger.FinishRemaining(success: true);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    ledger.FinishRemaining(success: false);
                }
            });

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result!;
    }

    private static void WriteHeader(
        IAnsiConsole console,
        ModulePipelinePlan plan,
        string? configLabel,
        ModulePipelineStep[] steps)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);
        static string Icon(string? s) => Esc(NormalizeIcon(s));

        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var versionText = BuildServices.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var packageLaneCount = (plan.ProjectBuilds?.Length ?? 0) + (plan.PackageBuilds?.Length ?? 0);
        var coordinatedRelease = packageLaneCount > 0 || plan.Release is not null;

        var title = unicode
            ? coordinatedRelease
                ? $"🛠️ PowerForge • Coordinated release • {plan.ModuleName} {versionText}"
                : $"🛠️ PowerForge • {plan.ModuleName} {versionText}"
            : coordinatedRelease
                ? $"PowerForge • Coordinated release • {plan.ModuleName} {versionText}"
                : $"PowerForge • {plan.ModuleName} {versionText}";
        console.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var iconColWidth = unicode ? 2 : 3;
        var info = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(BuildHeaderIconColumn(iconColWidth))
            .AddColumn(BuildHeaderKeyColumn())
            .AddColumn(BuildHeaderValueColumn());

        void AddInfoRow(string icon, string label, string valueMarkup)
            => info.AddRow($"[grey]{Icon(icon)}[/]", $"[grey]{Esc(label)}[/]", valueMarkup);

        var cfgText = string.IsNullOrWhiteSpace(configLabel) ? "(discovered)" : configLabel;
        AddInfoRow(unicode ? "⚙️" : "CFG", "Config", Esc(cfgText));
        AddInfoRow(unicode ? "📁" : "DIR", "Project", Esc(plan.ProjectRoot));

        if (coordinatedRelease)
        {
            var scope = packageLaneCount > 0
                ? $"PowerShell module + {packageLaneCount} NuGet package {(packageLaneCount == 1 ? "lane" : "lanes")}"
                : "PowerShell module";
            AddInfoRow(unicode ? "🧭" : "SCP", "Scope", Esc(scope));

            var buildOrder = plan.Release?.Configuration?.BuildOrder;
            if (buildOrder is { Length: > 0 })
                AddInfoRow(unicode ? "↕️" : "ORD", "Build order", Esc(FormatReleaseOrder(buildOrder)));

            var publishOrder = plan.Release?.Configuration?.PublishOrder;
            if (publishOrder is { Length: > 0 })
                AddInfoRow(unicode ? "🚀" : "PUB", "Publish order", Esc(FormatReleaseOrder(publishOrder)));
        }

        var stagingText = string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath;
        AddInfoRow(unicode ? "🧪" : "TMP", "Staging", Esc(stagingText));

        var frameworks = plan.BuildSpec.Frameworks is { Length: > 0 }
            ? string.Join(", ", plan.BuildSpec.Frameworks)
            : "(auto)";
        AddInfoRow(unicode ? "🧩" : "TFM", "Frameworks", Esc(frameworks));

        var docsEnabled = plan.DocumentationBuild?.Enable == true;
        AddInfoRow(unicode ? "📚" : "DOC", "Docs", docsEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

        var validations = new List<string>();
        if (plan.FileConsistencySettings?.Enable == true) validations.Add("File consistency");
        if (plan.CompatibilitySettings?.Enable == true) validations.Add("Compatibility");
        if (plan.ValidationSettings?.Enable == true) validations.Add("Module validation");
        AddInfoRow(
            unicode ? "🔎" : "VAL",
            "Validation",
            validations.Count == 0 ? "[grey]Disabled[/]" : Esc(string.Join(", ", validations)));

        AddInfoRow(
            unicode ? "📦" : "PKG",
            coordinatedRelease ? "Module artefacts" : "Artefacts",
            Esc((plan.Artefacts?.Length ?? 0).ToString()));
        AddInfoRow(
            unicode ? "🚀" : "PUB",
            coordinatedRelease ? "Module publishes" : "Publishes",
            Esc((plan.Publishes?.Length ?? 0).ToString()));
        AddInfoRow(unicode ? "📥" : "INS", "Install", plan.InstallEnabled ? Esc($"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}") : "[grey]Disabled[/]");

        AddInfoRow(unicode ? "🧭" : "STP", coordinatedRelease ? "Coordinated steps" : "Steps", Esc(steps.Length.ToString()));
        console.Write(info);
        console.WriteLine();
    }

    private static string FormatReleaseOrder(IEnumerable<string> values)
        => string.Join(
            " → ",
            values.Select(value => string.Equals(value, "PowerShellGallery", StringComparison.OrdinalIgnoreCase)
                ? "PowerShell Gallery"
                : value));

    private static string NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return string.Empty;
        return icon!.Replace("\uFE0F", string.Empty).Replace("\uFE0E", string.Empty);
    }

    private static TableColumn BuildHeaderIconColumn(int width)
    {
        var col = new TableColumn("i").NoWrap().Width(width);
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderKeyColumn()
    {
        var col = new TableColumn("k").NoWrap();
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderValueColumn()
    {
        var col = new TableColumn("v");
        col.Padding = new Padding(0, 0, 0, 0);
        return col;
    }

    private static SpectreProgressLedgerItem ToLedgerItem(
        PowerForgeReleaseProgressItem item)
    {
        return new SpectreProgressLedgerItem
        {
            Key = item.Key,
            GroupKey = item.GroupKey ?? item.Phase.ToString(),
            GroupTitle = item.GroupTitle ?? GetGroupTitle(item.Phase),
            GroupOrder = ((int)item.Phase * 100) + (item.GroupOrder ?? 0),
            Title = item.Title,
            Kind = item.Kind,
            Target = item.Target,
            CounterLabel = item.CounterLabel ?? GetCounterLabel(item),
            Position = item.Position,
            Total = item.Total
        };
    }

    private static ConsoleView ResolveView(ConsoleView requested)
    {
        if (requested != ConsoleView.Auto) return requested;
        var interactive = AnsiConsole.Profile.Capabilities.Interactive && !ConsoleEnvironment.IsCI;
        return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
    }

    private static string GetGroupTitle(PowerForgeReleaseProgressPhase phase)
        => phase == PowerForgeReleaseProgressPhase.Packages
            ? "Build and publish NuGet packages"
            : "Build PowerShell module";

    private static string GetCounterLabel(PowerForgeReleaseProgressItem item)
    {
        if (item.Phase != PowerForgeReleaseProgressPhase.Packages)
            return "Module";

        return item.Kind switch
        {
            nameof(ProjectBuildProgressPhase.PackageSigning) => "Package",
            nameof(ProjectBuildProgressPhase.NuGetPublish) => "Package",
            "GitHubAsset" => "Asset",
            _ => "Project"
        };
    }

    private sealed class SpectrePipelineProgressReporter : IModulePipelineProgressReporterV4
    {
        private readonly SpectreProgressLedger _ledger;
        private readonly IReadOnlyDictionary<string, SpectreProgressLedgerItem> _items;

        public SpectrePipelineProgressReporter(
            SpectreProgressLedger ledger,
            IReadOnlyDictionary<string, SpectreProgressLedgerItem> items)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public void StepStarting(ModulePipelineStep step)
            => Update(step, SpectreProgressLedgerState.Started);

        public void StepCompleted(ModulePipelineStep step)
            => Update(step, SpectreProgressLedgerState.Completed);

        public void StepFailed(ModulePipelineStep step, Exception error)
            => Update(step, SpectreProgressLedgerState.Failed, error?.Message);

        public void StepSkipped(ModulePipelineStep step)
            => Update(step, SpectreProgressLedgerState.Skipped);

        public void StepProgress(ModulePipelineStep step, double value, double maximum, string? detail = null)
        {
            if (step is null || !_items.TryGetValue(step.Key, out var item))
                return;

            item.ProgressValue = Math.Max(0, value);
            item.ProgressMaximum = Math.Max(0, maximum);
            _ledger.Update(item, SpectreProgressLedgerState.Started, detail);
        }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
        {
            if (items is null || items.Count == 0)
                return;

            _ledger.Plan(items
                .Where(static item => item is not null)
                .Select(ToLedgerItem));
        }

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
        {
            if (item is null)
                return;

            _ledger.Update(
                ToLedgerItem(item),
                state switch
                {
                    PowerForgeReleaseProgressItemState.Started => SpectreProgressLedgerState.Started,
                    PowerForgeReleaseProgressItemState.Completed => SpectreProgressLedgerState.Completed,
                    PowerForgeReleaseProgressItemState.Failed => SpectreProgressLedgerState.Failed,
                    PowerForgeReleaseProgressItemState.Skipped => SpectreProgressLedgerState.Skipped,
                    _ => SpectreProgressLedgerState.Planned
                },
                detail);
        }

        private void Update(
            ModulePipelineStep step,
            SpectreProgressLedgerState state,
            string? detail = null)
        {
            if (step is null || !_items.TryGetValue(step.Key, out var item))
                return;

            _ledger.Update(item, state, detail);
        }
    }
}
