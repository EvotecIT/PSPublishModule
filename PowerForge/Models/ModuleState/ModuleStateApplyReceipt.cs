using System;

namespace PowerForge;

internal sealed class ModuleStateApplyReceipt
{
    internal ModuleStateApplyReceipt(
        DateTimeOffset createdAtUtc,
        bool canApply,
        string? blockedReason,
        int actionCount,
        int findingCount,
        ModuleStateDeliveryCommand[] commands)
    {
        CreatedAtUtc = createdAtUtc;
        CanApply = canApply;
        BlockedReason = blockedReason;
        ActionCount = actionCount;
        FindingCount = findingCount;
        Commands = commands;
    }

    internal DateTimeOffset CreatedAtUtc { get; }

    internal bool CanApply { get; }

    internal string? BlockedReason { get; }

    internal int ActionCount { get; }

    internal int FindingCount { get; }

    internal ModuleStateDeliveryCommand[] Commands { get; }
}
