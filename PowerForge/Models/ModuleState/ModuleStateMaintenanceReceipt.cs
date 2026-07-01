using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateMaintenanceReceipt
{
    internal ModuleStateMaintenanceReceipt(
        string? source,
        IEnumerable<ModuleStateMaintenanceReceiptModule>? modules,
        ModuleStateDeliveryTransport? deliveryTransport = null,
        string? engine = null)
    {
        Source = string.IsNullOrWhiteSpace(source) ? null : source!.Trim();
        DeliveryTransport = deliveryTransport;
        Engine = string.IsNullOrWhiteSpace(engine) ? null : engine!.Trim();
        Modules = (modules ?? Array.Empty<ModuleStateMaintenanceReceiptModule>())
            .Where(static module => module is not null)
            .ToArray();
    }

    internal string? Source { get; }

    internal ModuleStateDeliveryTransport? DeliveryTransport { get; }

    internal string? Engine { get; }

    internal ModuleStateMaintenanceReceiptModule[] Modules { get; }
}
