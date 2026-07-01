using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

internal static class ManagedModuleSummaryWriter
{
    public static void Write(ManagedModuleInstallPlan plan)
    {
        var table = CreateTable("Managed module install plan");
        Add(table, "Name", plan.Name);
        Add(table, "Version", plan.Version);
        Add(table, "Action", plan.Action.ToString());
        Add(table, "Repository", FormatRepository(plan.RepositoryName, plan.RepositorySource));
        Add(table, "Module path", plan.ModulePath);
        Add(table, "Would write", plan.WouldWriteFiles ? "Yes" : "No");
        AddLicense(table, plan.LicenseAcceptanceRequired, plan.LicenseAccepted, plan.License);
        AnsiConsole.Write(table);
    }

    public static void Write(ManagedModuleInstallResult result)
    {
        var table = CreateTable("Managed module install");
        Add(table, "Name", result.Name);
        Add(table, "Version", result.Version);
        Add(table, "Status", result.Status.ToString());
        Add(table, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(table, "Module path", result.ModulePath);
        Add(table, "Files", result.FileCount.ToString());
        Add(table, "Elapsed", FormatElapsed(result.Elapsed));
        Add(table, "Receipt", result.ReceiptPath);
        AnsiConsole.Write(table);
    }

    public static void Write(ManagedModuleUpdatePlan plan)
    {
        var table = CreateTable("Managed module update plan");
        Add(table, "Name", plan.Name);
        Add(table, "Target version", plan.TargetVersion);
        Add(table, "Previous version", plan.PreviousVersion);
        Add(table, "Action", plan.Action.ToString());
        Add(table, "Repository", FormatRepository(plan.RepositoryName, plan.RepositorySource));
        Add(table, "Module path", plan.ModulePath);
        Add(table, "Would write", plan.WouldWriteFiles ? "Yes" : "No");
        AddLicense(table, plan.LicenseAcceptanceRequired, plan.LicenseAccepted, plan.License);
        AddSource(table, plan.SourcePolicySatisfied, plan.SourcePolicyReason);
        AddFamily(table, plan.FamilyActions);
        AnsiConsole.Write(table);
    }

    public static void Write(ManagedModuleUpdateResult result)
    {
        var table = CreateTable("Managed module update");
        Add(table, "Name", result.Name);
        Add(table, "Target version", result.TargetVersion);
        Add(table, "Previous version", result.PreviousVersion);
        Add(table, "Status", result.Status.ToString());
        Add(table, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(table, "Module path", result.ModulePath);
        Add(table, "Elapsed", FormatElapsed(result.Elapsed));
        Add(table, "Receipt", result.ReceiptPath);
        AddSource(table, result.SourcePolicySatisfied, result.SourcePolicyReason);
        AddFamily(table, result.FamilyResults);
        AnsiConsole.Write(table);
    }

    public static void Write(ManagedModulePublishResult result)
    {
        var table = CreateTable("Managed module publish");
        Add(table, "Name", result.Name);
        Add(table, "Version", result.Version);
        Add(table, "Published", result.Published ? "Yes" : "No");
        Add(table, "Duplicate", result.Duplicate ? "Yes" : "No");
        Add(table, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(table, "Publish source", result.PublishSource);
        Add(table, "Package path", result.PackagePath);
        Add(table, "Files", result.FileCount.ToString());
        Add(table, "Package bytes", result.PackageBytes.ToString());
        Add(table, "Elapsed", FormatElapsed(result.Elapsed));
        Add(table, "Message", result.Message);
        AnsiConsole.Write(table);
    }

    private static Table CreateTable(string title)
        => new Table()
            .Title(Markup.Escape(title))
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Field[/]"))
            .AddColumn(new TableColumn("[bold]Value[/]"));

    private static void Add(Table table, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        table.AddRow(Markup.Escape(label), Markup.Escape(value));
    }

    private static void AddSource(Table table, bool satisfied, string? reason)
    {
        if (satisfied && string.IsNullOrWhiteSpace(reason))
            return;

        Add(table, "Source policy", satisfied ? "Satisfied" : "Needs repair");
        Add(table, "Source reason", reason);
    }

    private static void AddLicense(Table table, bool required, bool accepted, string? license)
    {
        if (!required && string.IsNullOrWhiteSpace(license))
            return;

        Add(table, "License", license);
        if (required)
            Add(table, "License acceptance", accepted ? "Accepted" : "Required");
    }

    private static void AddFamily(Table table, IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> actions)
    {
        if (actions.Count == 0)
            return;

        Add(table, "Family actions", string.Join(", ", actions.Select(FormatFamilyPlanAction)));
    }

    private static void AddFamily(Table table, IReadOnlyList<ManagedModuleFamilyUpdateResult> results)
    {
        if (results.Count == 0)
            return;

        Add(table, "Family results", string.Join(", ", results.Select(FormatFamilyResult)));
    }

    private static string FormatFamilyPlanAction(ManagedModuleFamilyUpdatePlanItem action)
        => $"{action.Name}:{action.Action} -> {action.TargetVersion}";

    private static string FormatFamilyResult(ManagedModuleFamilyUpdateResult result)
        => $"{result.Name}:{result.Action} -> {result.TargetVersion}";

    private static string FormatRepository(string name, string source)
        => string.IsNullOrWhiteSpace(source) ? name : $"{name} ({source})";

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalMilliseconds < 1000
            ? $"{elapsed.TotalMilliseconds:0} ms"
            : $"{elapsed.TotalSeconds:0.00} s";
}
