using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleMonster.Layout;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

internal static class ManagedModuleSummaryWriter
{
    public static void Write(ManagedModuleInstallPlan plan)
    {
        var list = new KeyValueList();
        Add(list, "Name", plan.Name);
        Add(list, "Version", plan.Version);
        Add(list, "Action", plan.Action.ToString());
        Add(list, "Repository", FormatRepository(plan.RepositoryName, plan.RepositorySource));
        Add(list, "Module path", plan.ModulePath);
        Add(list, "Would write", plan.WouldWriteFiles ? "Yes" : "No");
        AddLicense(list, plan.LicenseAcceptanceRequired, plan.LicenseAccepted, plan.License);
        Write("Managed module install plan", list);
    }

    public static void Write(ManagedModuleInstallResult result)
    {
        var list = new KeyValueList();
        Add(list, "Name", result.Name);
        Add(list, "Version", result.Version);
        Add(list, "Status", result.Status.ToString());
        Add(list, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(list, "Module path", result.ModulePath);
        Add(list, "Files", result.FileCount.ToString());
        Add(list, "Elapsed", FormatElapsed(result.Elapsed));
        Add(list, "Receipt", result.ReceiptPath);
        Write("Managed module install", list);
    }

    public static void Write(ManagedModuleUpdatePlan plan)
    {
        var list = new KeyValueList();
        Add(list, "Name", plan.Name);
        Add(list, "Target version", plan.TargetVersion);
        Add(list, "Previous version", plan.PreviousVersion);
        Add(list, "Action", plan.Action.ToString());
        Add(list, "Repository", FormatRepository(plan.RepositoryName, plan.RepositorySource));
        Add(list, "Module path", plan.ModulePath);
        Add(list, "Would write", plan.WouldWriteFiles ? "Yes" : "No");
        AddLicense(list, plan.LicenseAcceptanceRequired, plan.LicenseAccepted, plan.License);
        AddSource(list, plan.SourcePolicySatisfied, plan.SourcePolicyReason);
        AddFamily(list, "Family actions", plan.FamilyActions.Select(static a => FormatFamily(a.Name, a.Action.ToString(), a.TargetVersion)));
        Write("Managed module update plan", list);
    }

    public static void Write(ManagedModuleUpdateResult result)
    {
        var list = new KeyValueList();
        Add(list, "Name", result.Name);
        Add(list, "Target version", result.TargetVersion);
        Add(list, "Previous version", result.PreviousVersion);
        Add(list, "Status", result.Status.ToString());
        Add(list, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(list, "Module path", result.ModulePath);
        Add(list, "Elapsed", FormatElapsed(result.Elapsed));
        Add(list, "Receipt", result.ReceiptPath);
        AddSource(list, result.SourcePolicySatisfied, result.SourcePolicyReason);
        AddFamily(list, "Family results", result.FamilyResults.Select(static r => FormatFamily(r.Name, r.Action.ToString(), r.TargetVersion)));
        Write("Managed module update", list);
    }

    public static void Write(ManagedModulePublishResult result)
    {
        var list = new KeyValueList();
        Add(list, "Name", result.Name);
        Add(list, "Version", result.Version);
        Add(list, "Published", result.Published ? "Yes" : "No", result.Published ? StatusKind.Pass : null);
        Add(list, "Duplicate", result.Duplicate ? "Yes" : "No", result.Duplicate ? StatusKind.Warn : null);
        Add(list, "Repository", FormatRepository(result.RepositoryName, result.RepositorySource));
        Add(list, "Publish source", result.PublishSource);
        Add(list, "Package path", result.PackagePath);
        Add(list, "Files", result.FileCount.ToString());
        Add(list, "Package bytes", result.PackageBytes.ToString());
        Add(list, "Elapsed", FormatElapsed(result.Elapsed));
        Add(list, "Message", result.Message);
        Write("Managed module publish", list);
    }

    private static void Write(string title, KeyValueList list)
        => ConsoleOutput.Write(new Panel(list, Markup.Escape(title)));

    private static void Add(KeyValueList list, string label, string? value, StatusKind? status = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        list.Add(label, Markup.Escape(value), status);
    }

    private static void AddSource(KeyValueList list, bool satisfied, string? reason)
    {
        if (satisfied && string.IsNullOrWhiteSpace(reason))
            return;

        Add(list, "Source policy", satisfied ? "Satisfied" : "Needs repair", satisfied ? StatusKind.Pass : StatusKind.Warn);
        Add(list, "Source reason", reason);
    }

    private static void AddLicense(KeyValueList list, bool required, bool accepted, string? license)
    {
        if (!required && string.IsNullOrWhiteSpace(license))
            return;

        Add(list, "License", license);
        if (required)
            Add(list, "License acceptance", accepted ? "Accepted" : "Required", accepted ? StatusKind.Pass : StatusKind.Warn);
    }

    private static void AddFamily(KeyValueList list, string label, IEnumerable<string> items)
    {
        var text = string.Join(", ", items);
        Add(list, label, text);
    }

    private static string FormatFamily(string name, string action, string targetVersion)
        => $"{name}:{action} -> {targetVersion}";

    private static string FormatRepository(string name, string source)
        => string.IsNullOrWhiteSpace(source) ? name : $"{name} ({source})";

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalMilliseconds < 1000
            ? $"{elapsed.TotalMilliseconds:0} ms"
            : $"{elapsed.TotalSeconds:0.00} s";
}
