namespace PowerForge;

internal sealed class ModuleStateApplyResult
{
    internal ModuleStateApplyResult(
        ModuleStateApplyReceipt receipt,
        ModuleStatePlan plan)
    {
        Receipt = receipt;
        Plan = plan;
    }

    internal ModuleStateApplyReceipt Receipt { get; }

    internal ModuleStatePlan Plan { get; }
}
