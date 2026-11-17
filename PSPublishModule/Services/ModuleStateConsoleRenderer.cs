using System;
using System.Linq;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

internal static class ModuleStateConsoleRenderer
{
    private const int MaxRows = 20;

    internal static void WriteInventory(ModuleStateInventoryResult inventory)
    {
        if (inventory is null)
            return;

        WriteRule("ModuleState inventory", inventory.InstalledModules.Length == 0 ? "warn" : "pass");
        var summary = new KeyValueList();
        summary.Add("Source", Esc(inventory.Source));
        summary.Add("Module paths", inventory.ModulePaths.Length.ToString());
        summary.Add("Inventory diagnostics", inventory.Diagnostics.Length.ToString());
        summary.Add("Inventory errors", inventory.Diagnostics.Count(static diagnostic => IsError(diagnostic.Severity)).ToString());
        summary.Add("Installed modules", inventory.InstalledModules.Length.ToString());
        summary.Add("Loaded modules", inventory.InstalledModules.Count(static module => module.IsLoaded).ToString());
        summary.Add("Effective import candidates", inventory.InstalledModules.Count(static module => module.IsEffectiveImportCandidate).ToString());
        ConsoleOutput.Write(summary);

        if (inventory.Diagnostics.Length > 0)
        {
            var diagnostics = CreateTable(("Severity", false), ("Code", false), ("Profile", false), ("Path", true), ("Message", true));
            foreach (var diagnostic in inventory.Diagnostics.Take(MaxRows))
            {
                diagnostics.AddRow(
                    Severity(diagnostic.Severity),
                    Esc(diagnostic.Code),
                    Esc(diagnostic.ProfileName),
                    Esc(diagnostic.Path),
                    Esc(diagnostic.Message));
            }

            ConsoleOutput.Write(diagnostics);
            WriteOverflow(inventory.Diagnostics.Length, "inventory diagnostics");
        }

        if (inventory.InstalledModules.Length > 0)
        {
            var modules = CreateTable(("Module", false), ("Version", false), ("Edition", false), ("Scope", false),
                ("Profile", false), ("Module root", true), ("Import", false), ("Loaded", false));
            foreach (var module in inventory.InstalledModules.Take(MaxRows))
            {
                modules.AddRow(
                    Esc(module.Name),
                    Esc(module.Version),
                    Esc(module.PowerShellEdition),
                    Esc(module.Scope),
                    Esc(module.ProfileName),
                    Esc(module.ModuleRoot),
                    YesNo(module.IsEffectiveImportCandidate, "pass"),
                    YesNo(module.IsLoaded, "warn"));
            }

            ConsoleOutput.Write(modules);
            WriteOverflow(inventory.InstalledModules.Length, "modules");
        }
    }

    internal static void WritePlan(ModuleStatePlanResult plan)
    {
        if (plan is null)
            return;

        WriteRule("ModuleState plan", plan.HasErrors ? "fail" : plan.Actions.Length == 0 ? "pass" : "warn");
        var summary = new KeyValueList();
        summary.Add("Actions", plan.Actions.Length.ToString());
        summary.Add("Findings", plan.Findings.Length.ToString());
        summary.Add("Errors", plan.Findings.Count(static finding => IsError(finding.Severity)).ToString());
        summary.Add("Inventory", Esc(plan.InventoryPath));
        summary.Add("Desired state", Esc(plan.DesiredStatePath));
        ConsoleOutput.Write(summary);

        WriteActions(plan.Actions);
        WriteFindings(plan.Findings);
    }

    internal static void WriteTest(ModuleStateTestResult result, bool includePlan = true)
    {
        if (result is null)
            return;

        WriteRule(result.IsCompliant ? "ModuleState compliant" : "ModuleState not compliant", result.IsCompliant ? "pass" : "fail");
        var summary = new KeyValueList();
        summary.Add("Required actions", result.RequiredActionCount.ToString());
        summary.Add("Error findings", result.ErrorCount.ToString());
        ConsoleOutput.Write(summary);
        if (includePlan)
            WritePlan(result.Plan);
    }

    internal static void WriteApply(ModuleStateApplyResult result)
    {
        if (result is null)
            return;

        WriteRule(result.CanApply ? "ModuleState apply" : "ModuleState apply blocked", result.CanApply ? "pass" : "fail");
        var summary = new KeyValueList();
        AddVerdict(summary, "Can apply", result.CanApply);
        summary.Add("Actions", result.ActionCount.ToString());
        summary.Add("Findings", result.FindingCount.ToString());
        summary.Add("Execution requested", result.ExecutionRequested ? "yes" : "no");
        if (result.ExecutionRequested)
        {
            AddVerdict(summary, "Execution succeeded", result.ExecutionSucceeded);
            AddVerdict(summary, "Converged", result.Converged);
            if (result.PostApplyTest is not null)
                AddVerdict(summary, "Post-apply compliant", result.PostApplyTest.IsCompliant);
        }
        if (!string.IsNullOrWhiteSpace(result.BlockedReason))
            summary.Add("Blocked reason", Esc(result.BlockedReason));
        if (!string.IsNullOrWhiteSpace(result.ReceiptPath))
            summary.Add("Receipt", Esc(result.ReceiptPath));
        if (!string.IsNullOrWhiteSpace(result.MaintenanceReceiptOutputPath))
            summary.Add("Maintenance receipt", Esc(result.MaintenanceReceiptOutputPath));
        ConsoleOutput.Write(summary);

        if (result.Commands.Length > 0)
        {
            var commands = CreateTable(("Command", false), ("Module", false), ("Policy", false), ("Repair", false), ("Text", true));
            foreach (var command in result.Commands.Take(MaxRows))
            {
                commands.AddRow(
                    Esc(command.CommandName),
                    Esc(command.ModuleName),
                    Esc(command.VersionPolicy),
                    YesNo(command.IsRepair, "warn"),
                    Esc(command.CommandText));
            }

            ConsoleOutput.Write(commands);
            WriteOverflow(result.Commands.Length, "commands");
        }

        if (result.ExecutionResults.Length > 0)
        {
            var executions = CreateTable(("Operation", false), ("Target", true), ("Repository", false), ("Transport", false),
                ("Performed", false), ("Status", false), ("Dependencies", false), ("Details", true));
            foreach (var execution in result.ExecutionResults)
            {
                executions.AddRow(
                    Esc(execution.Operation),
                    Esc(execution.TargetPath),
                    Esc(execution.RepositoryName),
                    Esc(FormatExecutionTransport(execution)),
                    YesNo(execution.OperationPerformed, "pass"),
                    Esc(FormatExecutionStatuses(execution)),
                    execution.DependencyResults.Length.ToString(),
                    Esc(FormatExecutionDetails(execution)));
            }

            ConsoleOutput.Write(executions);
        }
    }

    private static void WriteActions(ModuleStatePlanActionResult[] actions)
    {
        if (actions.Length == 0)
            return;

        var table = CreateTable(("Action", false), ("Module", false), ("Installed", false), ("Policy", false),
            ("Target", false), ("License", false), ("Reason", true));
        foreach (var action in actions.Take(MaxRows))
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

        ConsoleOutput.Write(table);
        WriteOverflow(actions.Length, "actions");
    }

    private static void WriteFindings(ModuleStateConflictFindingResult[] findings)
    {
        if (findings.Length == 0)
            return;

        var table = CreateTable(("Severity", false), ("Code", false), ("Modules", false), ("Placement", true), ("Message", true));
        foreach (var finding in findings.Take(MaxRows))
        {
            table.AddRow(
                Severity(finding.Severity),
                Esc(finding.Code),
                Esc(string.Join(", ", finding.ModuleNames ?? Array.Empty<string>())),
                Esc(FormatFindingPlacement(finding)),
                Esc(finding.Message));
        }

        ConsoleOutput.Write(table);
        WriteOverflow(findings.Length, "findings");
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

    private static void WriteRule(string title, string role)
        => ConsoleOutput.Write(new Rule($"[{role}]{Markup.Escape(title)}[/]"));

    private static Table CreateTable(params (string Header, bool Wrap)[] columns)
    {
        // Keep every column and every wrapped line: paths and messages are the evidence people act on.
        // When the grid cannot fit, the remaining columns follow in another grid keyed by the first column.
        var table = new Table { KeepAllColumns = true };
        for (var i = 0; i < columns.Length; i++)
        {
            var column = new TableColumn(Markup.Escape(columns[i].Header)) { IsKey = i == 0 };
            if (columns[i].Wrap)
                column.MaxLines = TableColumn.Unlimited;
            else
                column.Wrap = false;
            table.AddColumn(column);
        }
        return table;
    }

    private static bool IsError(string? severity)
        => string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase);

    private static TableCell Severity(string? severity)
        => TableCell.ForStatus(IsError(severity) ? StatusKind.Fail : StatusKind.Warn, severity ?? string.Empty);

    private static string YesNo(bool value, string role)
        => value ? $"[{role}]yes[/]" : "[muted]no[/]";

    private static void AddVerdict(KeyValueList list, string label, bool value)
        => list.Add(label, value ? "yes" : "no", value ? StatusKind.Pass : StatusKind.Fail);

    private static string Esc(string? value)
        => Markup.Escape(value ?? string.Empty);

    private static void WriteOverflow(int count, string label)
    {
        if (count > MaxRows)
            ConsoleOutput.WriteMarkupLine($"[muted]Showing {MaxRows} of {count} {Markup.Escape(label)}.[/]");
    }
}