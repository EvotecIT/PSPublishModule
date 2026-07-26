namespace PowerForge;

internal sealed class ModuleStateApplyResult
{
    internal ModuleStateApplyResult(
        ModuleStateApplyReceipt receipt,
        ModuleStatePlan plan,
        ModuleStateDeliveryOptions deliveryOptions)
    {
        Receipt = receipt;
        Plan = plan;
        DeliveryOptions = deliveryOptions;
    }

    internal ModuleStateApplyReceipt Receipt { get; }

    internal ModuleStatePlan Plan { get; }

    internal ModuleStateDeliveryOptions DeliveryOptions { get; }
}
