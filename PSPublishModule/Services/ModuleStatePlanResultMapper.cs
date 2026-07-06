using System;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStatePlanResultMapper
{
    internal static ModuleStatePlanResult ToCmdletResult(
        ModuleStatePlan plan,
        string inventoryPath,
        string desiredStatePath,
        string[]? maintenanceReceiptPaths = null)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        return new ModuleStatePlanResult
        {
            InventoryPath = inventoryPath,
            DesiredStatePath = desiredStatePath,
            MaintenanceReceiptPaths = maintenanceReceiptPaths ?? Array.Empty<string>(),
            HasErrors = plan.HasErrors,
            Actions = plan.Actions.Select(static action => new ModuleStatePlanActionResult
            {
                Kind = action.Kind.ToString(),
                ModuleName = action.ModuleName,
                InstalledVersion = action.InstalledVersion,
                VersionPolicy = action.VersionPolicy,
                Reason = action.Reason,
                IsRepair = action.IsRepair,
                Force = action.Force,
                IncludePrerelease = action.IncludePrerelease,
                AcceptLicense = action.AcceptLicense,
                AllowClobber = action.AllowClobber,
                SkipDependencyCheck = action.SkipDependencyCheck,
                TargetScope = action.TargetScope,
                TargetPath = action.TargetPath,
                TargetRepository = action.TargetRepository,
                TargetRepositorySource = action.TargetRepositorySource,
                ExpectedPackageSha256 = action.ExpectedPackageSha256,
                License = action.License,
                LicenseAcceptanceRequired = action.LicenseAcceptanceRequired,
                LicenseAccepted = action.LicenseAccepted
            }).ToArray(),
            Findings = plan.Findings.Select(static finding => new ModuleStateConflictFindingResult
            {
                Severity = finding.Severity.ToString(),
                Code = finding.Code,
                Message = finding.Message,
                FamilyName = finding.FamilyName,
                ModuleNames = finding.ModuleNames,
                Versions = finding.Versions
            }).ToArray()
        };
    }

    internal static ModuleStatePlan ToCorePlan(ModuleStatePlanResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return new ModuleStatePlan(
            (result.Actions ?? Array.Empty<ModuleStatePlanActionResult>()).Select(static action => new ModuleStatePlanAction(
                ParseEnum<ModuleStatePlanActionKind>(action.Kind, nameof(action.Kind)),
                action.ModuleName,
                action.InstalledVersion,
                action.VersionPolicy,
                action.Reason,
                isRepair: action.IsRepair,
                force: action.Force,
                targetScope: action.TargetScope,
                targetPath: action.TargetPath,
                targetRepository: action.TargetRepository,
                expectedPackageSha256: action.ExpectedPackageSha256,
                license: action.License,
                licenseAcceptanceRequired: action.LicenseAcceptanceRequired,
                licenseAccepted: action.LicenseAccepted,
                includePrerelease: action.IncludePrerelease,
                acceptLicense: action.AcceptLicense,
                allowClobber: action.AllowClobber,
                skipDependencyCheck: action.SkipDependencyCheck,
                targetRepositorySource: action.TargetRepositorySource)).ToArray(),
            (result.Findings ?? Array.Empty<ModuleStateConflictFindingResult>()).Select(static finding => new ModuleStateConflictFinding(
                ParseEnum<ModuleStateConflictSeverity>(finding.Severity, nameof(finding.Severity)),
                finding.Code,
                finding.Message,
                finding.FamilyName,
                finding.ModuleNames ?? Array.Empty<string>(),
                finding.Versions ?? Array.Empty<string>())).ToArray());
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw new InvalidOperationException($"ModuleState plan artifact contains unsupported {propertyName} value '{value}'.");
    }
}
