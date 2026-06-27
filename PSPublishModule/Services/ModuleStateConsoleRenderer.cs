using System;
using System.Linq;
using Spectre.Console;

namespace PSPublishModule;

internal static class ModuleStateConsoleRenderer
{
    internal static void WriteInventory(ModuleStateInventoryResult inventory)
    {
        if (inventory is null)
            return;

        WriteRule("ModuleState inventory", inventory.InstalledModules.Length == 0 ? "yellow" : "green");
        var summary = SummaryTable();
        summary.AddRow("Source", Esc(inventory.Source));
        summary.AddRow("Module paths", inventory.ModulePaths.Length.ToString());
        summary.AddRow("Installed modules", inventory.InstalledModules.Length.ToString());
        summary.AddRow("Loaded modules", inventory.InstalledModules.Count(static module => module.IsLoaded).ToString());
        summary.AddRow("Effective import candidates", inventory.InstalledModules.Count(static module => module.IsEffectiveImportCandidate).ToString());
        AnsiConsole.Write(summary);

        if (inventory.InstalledModules.Length == 0)
            return;

        var modules = new Table().Border(Border());
        modules.AddColumn(new TableColumn("Module").NoWrap());
        modules.AddColumn(new TableColumn("Version").NoWrap());
        modules.AddColumn(new TableColumn("Scope").NoWrap());
        modules.AddColumn(new TableColumn("Source").NoWrap());
        modules.AddColumn(new TableColumn("Import").NoWrap());
        modules.AddColumn(new TableColumn("Loaded").NoWrap());
        foreach (var module in inventory.InstalledModules.Take(20))
        {
            modules.AddRow(
                Esc(module.Name),
                Esc(module.Version),
                Esc(module.Scope),
                Esc(module.SourceRepository),
                module.IsEffectiveImportCandidate ? "[green]yes[/]" : "[dim]no[/]",
                module.IsLoaded ? "[yellow]yes[/]" : "[dim]no[/]");
        }

        AnsiConsole.Write(modules);
        WriteOverflow(inventory.InstalledModules.Length, 20, "modules");
    }

    internal static void WritePlan(ModuleStatePlanResult plan)
    {
        if (plan is null)
            return;

        var color = plan.HasErrors ? "red" : plan.Actions.Length == 0 ? "green" : "yellow";
        WriteRule("ModuleState plan", color);
        var summary = SummaryTable();
        summary.AddRow("Actions", plan.Actions.Length.ToString());
        summary.AddRow("Findings", plan.Findings.Length.ToString());
        summary.AddRow("Errors", plan.Findings.Count(static finding => string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToString());
        summary.AddRow("Inventory", Esc(plan.InventoryPath));
        summary.AddRow("Desired state", Esc(plan.DesiredStatePath));
        AnsiConsole.Write(summary);

        WriteActions(plan.Actions);
        WriteFindings(plan.Findings);
    }

    internal static void WriteTest(ModuleStateTestResult result, bool includePlan = true)
    {
        if (result is null)
            return;

        WriteRule(result.IsCompliant ? "ModuleState compliant" : "ModuleState not compliant", result.IsCompliant ? "green" : "red");
        var summary = SummaryTable();
        summary.AddRow("Required actions", result.RequiredActionCount.ToString());
        summary.AddRow("Error findings", result.ErrorCount.ToString());
        AnsiConsole.Write(summary);
        if (includePlan)
            WritePlan(result.Plan);
    }

    internal static void WriteApply(ModuleStateApplyResult result)
    {
        if (result is null)
            return;

        WriteRule(result.CanApply ? "ModuleState apply" : "ModuleState apply blocked", result.CanApply ? "green" : "red");
        var summary = SummaryTable();
        summary.AddRow("Can apply", result.CanApply ? "[green]yes[/]" : "[red]no[/]");
        summary.AddRow("Actions", result.ActionCount.ToString());
        summary.AddRow("Findings", result.FindingCount.ToString());
        summary.AddRow("Execution requested", result.ExecutionRequested ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(result.BlockedReason))
            summary.AddRow("Blocked reason", Esc(result.BlockedReason));
        if (!string.IsNullOrWhiteSpace(result.ReceiptPath))
            summary.AddRow("Receipt", Esc(result.ReceiptPath));
        if (!string.IsNullOrWhiteSpace(result.MaintenanceReceiptOutputPath))
            summary.AddRow("Maintenance receipt", Esc(result.MaintenanceReceiptOutputPath));
        AnsiConsole.Write(summary);

        if (result.Commands.Length > 0)
        {
            var commands = new Table().Border(Border());
            commands.AddColumn(new TableColumn("Command").NoWrap());
            commands.AddColumn(new TableColumn("Module").NoWrap());
            commands.AddColumn(new TableColumn("Policy").NoWrap());
            commands.AddColumn(new TableColumn("Repair").NoWrap());
            commands.AddColumn(new TableColumn("Text"));
            foreach (var command in result.Commands.Take(20))
            {
                commands.AddRow(
                    Esc(command.CommandName),
                    Esc(command.ModuleName),
                    Esc(command.VersionPolicy),
                    command.IsRepair ? "[yellow]yes[/]" : "[dim]no[/]",
                    Esc(command.CommandText));
            }

            AnsiConsole.Write(commands);
            WriteOverflow(result.Commands.Length, 20, "commands");
        }

        if (result.ExecutionResults.Length > 0)
        {
            var executions = new Table().Border(Border());
            executions.AddColumn(new TableColumn("Operation").NoWrap());
            executions.AddColumn(new TableColumn("Repository").NoWrap());
            executions.AddColumn(new TableColumn("Performed").NoWrap());
            executions.AddColumn(new TableColumn("Status").NoWrap());
            executions.AddColumn(new TableColumn("Dependencies").NoWrap());
            executions.AddColumn(new TableColumn("Details"));
            foreach (var execution in result.ExecutionResults)
            {
                executions.AddRow(
                    Esc(execution.Operation),
                    Esc(execution.RepositoryName),
                    execution.OperationPerformed ? "[green]yes[/]" : "[dim]no[/]",
                    Esc(FormatExecutionStatuses(execution)),
                    execution.DependencyResults.Length.ToString(),
                    Esc(FormatExecutionDetails(execution)));
            }

            AnsiConsole.Write(executions);
        }
    }

    private static void WriteActions(ModuleStatePlanActionResult[] actions)
    {
        if (actions.Length == 0)
            return;

        var table = new Table().Border(Border());
        table.AddColumn(new TableColumn("Action").NoWrap());
        table.AddColumn(new TableColumn("Module").NoWrap());
        table.AddColumn(new TableColumn("Installed").NoWrap());
        table.AddColumn(new TableColumn("Policy").NoWrap());
        table.AddColumn(new TableColumn("Target").NoWrap());
        table.AddColumn(new TableColumn("Reason"));
        foreach (var action in actions.Take(20))
        {
            table.AddRow(
                Esc(action.Kind),
                Esc(action.ModuleName),
                Esc(action.InstalledVersion),
                Esc(action.VersionPolicy),
                Esc(FormatTarget(action)),
                Esc(action.Reason));
        }

        AnsiConsole.Write(table);
        WriteOverflow(actions.Length, 20, "actions");
    }

    private static void WriteFindings(ModuleStateConflictFindingResult[] findings)
    {
        if (findings.Length == 0)
            return;

        var table = new Table().Border(Border());
        table.AddColumn(new TableColumn("Severity").NoWrap());
        table.AddColumn(new TableColumn("Code").NoWrap());
        table.AddColumn(new TableColumn("Modules").NoWrap());
        table.AddColumn(new TableColumn("Message"));
        foreach (var finding in findings.Take(20))
        {
            var severityColor = string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
            table.AddRow(
                $"[{severityColor}]{Esc(finding.Severity)}[/]",
                Esc(finding.Code),
                Esc(string.Join(", ", finding.ModuleNames ?? Array.Empty<string>())),
                Esc(finding.Message));
        }

        AnsiConsole.Write(table);
        WriteOverflow(findings.Length, 20, "findings");
    }

    private static string FormatTarget(ModuleStatePlanActionResult action)
    {
        var parts = new[]
        {
            action.TargetScope,
            action.TargetRepository,
            action.TargetPath
        }.Where(static part => !string.IsNullOrWhiteSpace(part));

        return string.Join(" / ", parts);
    }

    internal static string FormatExecutionStatuses(ModuleStateDeliveryExecutionResult execution)
    {
        if (execution is null || execution.DependencyResults.Length == 0)
            return execution?.OperationPerformed == true ? "Performed" : "Skipped";

        return string.Join(
            ", ",
            execution.DependencyResults
                .Select(static dependency => dependency.Status)
                .Where(static status => !string.IsNullOrWhiteSpace(status))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static string FormatExecutionDetails(ModuleStateDeliveryExecutionResult execution)
    {
        if (execution is null)
            return string.Empty;

        var details = execution.DependencyResults
            .Select(static dependency => dependency.Message)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (details.Length > 0)
            return string.Join("; ", details);

        return execution.OperationPerformed
            ? "Operation performed."
            : "Operation skipped or no changes were required.";
    }

    private static void WriteRule(string title, string color)
        => AnsiConsole.Write(new Rule($"[{color}]{Markup.Escape(title)}[/]").LeftJustified());

    private static Table SummaryTable()
        => new Table()
            .Border(Border())
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

    private static TableBorder Border()
        => AnsiConsole.Profile.Capabilities.Unicode ? TableBorder.Rounded : TableBorder.Simple;

    private static string Esc(string? value)
        => Markup.Escape(value ?? string.Empty);

    private static void WriteOverflow(int count, int shown, string label)
    {
        if (count > shown)
            AnsiConsole.MarkupLine("[dim]Showing {0} of {1} {2}.[/]", shown, count, Markup.Escape(label));
    }
}
