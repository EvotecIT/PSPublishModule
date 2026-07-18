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
        summary.AddRow("Inventory diagnostics", inventory.Diagnostics.Length.ToString());
        summary.AddRow("Inventory errors", inventory.Diagnostics.Count(static diagnostic => string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToString());
        summary.AddRow("Installed modules", inventory.InstalledModules.Length.ToString());
        summary.AddRow("Loaded modules", inventory.InstalledModules.Count(static module => module.IsLoaded).ToString());
        summary.AddRow("Effective import candidates", inventory.InstalledModules.Count(static module => module.IsEffectiveImportCandidate).ToString());
        AnsiConsole.Write(summary);

        if (inventory.Diagnostics.Length > 0)
        {
            var diagnostics = new Table().Border(Border());
            diagnostics.AddColumn(new TableColumn("Severity").NoWrap());
            diagnostics.AddColumn(new TableColumn("Code").NoWrap());
            diagnostics.AddColumn(new TableColumn("Profile").NoWrap());
            diagnostics.AddColumn(new TableColumn("Path"));
            diagnostics.AddColumn(new TableColumn("Message"));
            foreach (var diagnostic in inventory.Diagnostics.Take(20))
            {
                var color = string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
                diagnostics.AddRow(
                    $"[{color}]{Esc(diagnostic.Severity)}[/]",
                    Esc(diagnostic.Code),
                    Esc(diagnostic.ProfileName),
                    Esc(diagnostic.Path),
                    Esc(diagnostic.Message));
            }

            AnsiConsole.Write(diagnostics);
            WriteOverflow(inventory.Diagnostics.Length, 20, "inventory diagnostics");
        }

        if (inventory.InstalledModules.Length > 0)
        {
            var modules = new Table().Border(Border());
            modules.AddColumn(new TableColumn("Module").NoWrap());
            modules.AddColumn(new TableColumn("Version").NoWrap());
            modules.AddColumn(new TableColumn("Edition").NoWrap());
            modules.AddColumn(new TableColumn("Scope").NoWrap());
            modules.AddColumn(new TableColumn("Profile").NoWrap());
            modules.AddColumn(new TableColumn("Module root"));
            modules.AddColumn(new TableColumn("Import").NoWrap());
            modules.AddColumn(new TableColumn("Loaded").NoWrap());
            foreach (var module in inventory.InstalledModules.Take(20))
            {
                modules.AddRow(
                    Esc(module.Name),
                    Esc(module.Version),
                    Esc(module.PowerShellEdition),
                    Esc(module.Scope),
                    Esc(module.ProfileName),
                    Esc(module.ModuleRoot),
                    module.IsEffectiveImportCandidate ? "[green]yes[/]" : "[dim]no[/]",
                    module.IsLoaded ? "[yellow]yes[/]" : "[dim]no[/]");
            }

            AnsiConsole.Write(modules);
            WriteOverflow(inventory.InstalledModules.Length, 20, "modules");
        }
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
        if (result.ExecutionRequested)
        {
            summary.AddRow("Execution succeeded", result.ExecutionSucceeded ? "[green]yes[/]" : "[red]no[/]");
            summary.AddRow("Converged", result.Converged ? "[green]yes[/]" : "[red]no[/]");
            if (result.PostApplyTest is not null)
                summary.AddRow("Post-apply compliant", result.PostApplyTest.IsCompliant ? "[green]yes[/]" : "[red]no[/]");
        }
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
            executions.AddColumn(new TableColumn("Target"));
            executions.AddColumn(new TableColumn("Repository").NoWrap());
            executions.AddColumn(new TableColumn("Transport").NoWrap());
            executions.AddColumn(new TableColumn("Performed").NoWrap());
            executions.AddColumn(new TableColumn("Status").NoWrap());
            executions.AddColumn(new TableColumn("Dependencies").NoWrap());
            executions.AddColumn(new TableColumn("Details"));
            foreach (var execution in result.ExecutionResults)
            {
                executions.AddRow(
                    Esc(execution.Operation),
                    Esc(execution.TargetPath),
                    Esc(execution.RepositoryName),
                    Esc(FormatExecutionTransport(execution)),
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
        table.AddColumn(new TableColumn("License").NoWrap());
        table.AddColumn(new TableColumn("Reason"));
        foreach (var action in actions.Take(20))
        {
            table.AddRow(
                Esc(action.Kind),
                Esc(action.ModuleName),
                Esc(action.InstalledVersion),
                Esc(action.VersionPolicy),
                Esc(FormatTarget(action)),
                Esc(FormatLicense(action)),
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
        table.AddColumn(new TableColumn("Placement"));
        table.AddColumn(new TableColumn("Message"));
        foreach (var finding in findings.Take(20))
        {
            var severityColor = string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
            table.AddRow(
                $"[{severityColor}]{Esc(finding.Severity)}[/]",
                Esc(finding.Code),
                Esc(string.Join(", ", finding.ModuleNames ?? Array.Empty<string>())),
                Esc(FormatFindingPlacement(finding)),
                Esc(finding.Message));
        }

        AnsiConsole.Write(table);
        WriteOverflow(findings.Length, 20, "findings");
    }

    private static string FormatTarget(ModuleStatePlanActionResult action)
    {
        var parts = new[]
        {
            action.TargetPowerShellEdition,
            action.TargetScope,
            action.TargetProfileName,
            action.TargetRepository,
            action.TargetModuleRoot,
            action.TargetPath
        }.Where(static part => !string.IsNullOrWhiteSpace(part));

        return string.Join(" / ", parts);
    }

    private static string FormatFindingPlacement(ModuleStateConflictFindingResult finding)
        => string.Join(
            " / ",
            new[] { finding.PowerShellEdition, finding.Scope, finding.ProfileName, finding.Path }
                .Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string FormatLicense(ModuleStatePlanActionResult action)
    {
        if (!action.LicenseAcceptanceRequired)
            return string.Empty;

        return action.LicenseAccepted ? "accepted" : "required";
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
        if (!execution.Succeeded && !string.IsNullOrWhiteSpace(execution.ErrorMessage))
            return execution.ErrorMessage!;

        var details = execution.DependencyResults
            .Select(static dependency => dependency.Message)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (details.Length > 0)
            return string.Join("; ", details);
        if (!string.IsNullOrWhiteSpace(execution.DeliveryTransportReason))
            return execution.DeliveryTransportReason;

        return execution.OperationPerformed
            ? "Operation performed."
            : "Operation skipped or no changes were required.";
    }

    internal static string FormatExecutionTransport(ModuleStateDeliveryExecutionResult execution)
    {
        if (execution is null)
            return string.Empty;

        return execution.RequestedTransport == execution.EffectiveTransport
            ? execution.EffectiveTransport.ToString()
            : $"{execution.RequestedTransport} -> {execution.EffectiveTransport}";
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
