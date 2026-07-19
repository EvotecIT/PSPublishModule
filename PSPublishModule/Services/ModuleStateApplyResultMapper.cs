using System;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateApplyResultMapper
{
    internal static ModuleStateApplyResult ToCmdletResult(
        PowerForge.ModuleStateApplyResult result,
        string? receiptPath,
        string? maintenanceReceiptOutputPath = null,
        bool executionRequested = false,
        ModuleStateDeliveryExecutionResult[]? executionResults = null,
        ModuleStateInventoryResult? postApplyInventory = null,
        ModuleStatePlanResult? postApplyPlan = null,
        ModuleStateTestResult? postApplyTest = null,
        bool executionSucceeded = false,
        bool converged = false)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var receipt = result.Receipt;
        return new ModuleStateApplyResult
        {
            CanApply = receipt.CanApply,
            BlockedReason = receipt.BlockedReason,
            ReceiptPath = receiptPath,
            MaintenanceReceiptOutputPath = maintenanceReceiptOutputPath,
            ActionCount = receipt.ActionCount,
            FindingCount = receipt.FindingCount,
            Commands = receipt.Commands.Select(static command => new ModuleStateDeliveryCommandResult
            {
                ActionKind = command.ActionKind.ToString(),
                ModuleName = command.ModuleName,
                VersionPolicy = command.VersionPolicy,
                IsRepair = command.IsRepair,
                Force = command.Force,
                CommandName = command.CommandName,
                Arguments = command.Arguments,
                CommandText = command.CommandText
            }).ToArray(),
            ExecutionRequested = executionRequested,
            ExecutionResults = executionResults ?? Array.Empty<ModuleStateDeliveryExecutionResult>(),
            PostApplyInventory = postApplyInventory,
            PostApplyPlan = postApplyPlan,
            PostApplyTest = postApplyTest,
            ExecutionSucceeded = executionSucceeded,
            Converged = converged
        };
    }
}
