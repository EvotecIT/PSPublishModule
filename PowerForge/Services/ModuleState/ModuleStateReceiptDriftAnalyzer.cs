using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateReceiptDriftAnalyzer
{
    internal ModuleStateConflictFinding[] Analyze(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateMaintenanceReceipt> receipts)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var findings = new List<ModuleStateConflictFinding>();
        foreach (var receipt in receipts ?? Array.Empty<ModuleStateMaintenanceReceipt>())
        {
            foreach (var receiptModule in receipt.Modules)
            {
                var installedModules = inventory.InstalledModules
                    .Where(module => string.Equals(module.Name, receiptModule.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                AddMissingOrVersionDriftFinding(findings, receipt, receiptModule, installedModules);
                AddSourceDriftFinding(findings, receipt, receiptModule, installedModules);
                AddScopeDriftFinding(findings, receipt, receiptModule, installedModules);
            }
        }

        return findings.ToArray();
    }

    private static void AddMissingOrVersionDriftFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateMaintenanceReceipt receipt,
        ModuleStateMaintenanceReceiptModule receiptModule,
        ModuleStateInstalledModule[] installedModules)
    {
        if (installedModules.Length == 0)
        {
            findings.Add(CreateFinding(
                ModuleStateConflictSeverity.Error,
                "ModuleState.ReceiptModuleMissing",
                $"Maintenance receipt expects module '{receiptModule.Name}' version {receiptModule.Version}, but the module is not installed.",
                receipt,
                receiptModule,
                new[] { receiptModule.Version }));
            return;
        }

        if (installedModules.Any(module => VersionsEqual(module.Version, receiptModule.Version)))
            return;

        var installedVersions = installedModules
            .Select(static module => module.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static version => ModuleStateVersion.TryParse(version, out var parsed) ? parsed : default)
            .ToArray();
        findings.Add(CreateFinding(
            ModuleStateConflictSeverity.Error,
            "ModuleState.ReceiptVersionDrift",
            $"Maintenance receipt expects module '{receiptModule.Name}' version {receiptModule.Version}, but installed versions are: {string.Join(", ", installedVersions)}.",
            receipt,
            receiptModule,
            new[] { receiptModule.Version }.Concat(installedVersions).ToArray()));
    }

    private static void AddSourceDriftFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateMaintenanceReceipt receipt,
        ModuleStateMaintenanceReceiptModule receiptModule,
        ModuleStateInstalledModule[] installedModules)
    {
        if (string.IsNullOrWhiteSpace(receiptModule.SourceRepository))
            return;

        var versionMatches = installedModules
            .Where(module => VersionsEqual(module.Version, receiptModule.Version))
            .ToArray();
        if (versionMatches.Length == 0)
            return;

        if (versionMatches.Any(module => string.Equals(module.SourceRepository, receiptModule.SourceRepository, StringComparison.OrdinalIgnoreCase)))
            return;

        findings.Add(CreateFinding(
            ModuleStateConflictSeverity.Error,
            "ModuleState.ReceiptSourceDrift",
            $"Maintenance receipt expects module '{receiptModule.Name}' version {receiptModule.Version} from source '{receiptModule.SourceRepository}', but the installed matching version came from a different or unknown source.",
            receipt,
            receiptModule,
            new[] { receiptModule.Version }));
    }

    private static void AddScopeDriftFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateMaintenanceReceipt receipt,
        ModuleStateMaintenanceReceiptModule receiptModule,
        ModuleStateInstalledModule[] installedModules)
    {
        if (string.IsNullOrWhiteSpace(receiptModule.Scope))
            return;

        var versionMatches = installedModules
            .Where(module => VersionsEqual(module.Version, receiptModule.Version))
            .ToArray();
        if (versionMatches.Length == 0)
            return;

        var samePlacementMatches = versionMatches
            .Where(module => string.IsNullOrWhiteSpace(receiptModule.SourceRepository) ||
                             string.Equals(module.SourceRepository, receiptModule.SourceRepository, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (samePlacementMatches.Any(module => string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)))
            return;

        findings.Add(CreateFinding(
            ModuleStateConflictSeverity.Error,
            "ModuleState.ReceiptScopeDrift",
            $"Maintenance receipt expects module '{receiptModule.Name}' version {receiptModule.Version} in scope '{receiptModule.Scope}', but that version is installed in a different or unknown scope.",
            receipt,
            receiptModule,
            new[] { receiptModule.Version }));
    }

    private static ModuleStateConflictFinding CreateFinding(
        ModuleStateConflictSeverity severity,
        string code,
        string message,
        ModuleStateMaintenanceReceipt receipt,
        ModuleStateMaintenanceReceiptModule module,
        string[] versions)
    {
        var receiptSuffix = string.IsNullOrWhiteSpace(receipt.Source)
            ? string.Empty
            : $" Receipt source: {receipt.Source}.";
        return new ModuleStateConflictFinding(
            severity,
            code,
            message + receiptSuffix,
            string.Empty,
            new[] { module.Name },
            versions);
    }

    private static bool VersionsEqual(string installedVersion, string receiptVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(receiptVersion, out var expected))
        {
            return installed.CompareTo(expected) == 0;
        }

        return string.Equals(installedVersion, receiptVersion, StringComparison.OrdinalIgnoreCase);
    }
}
