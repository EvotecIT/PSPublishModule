using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateMaintenanceReceipt
{
    internal ModuleStateMaintenanceReceipt(string? source, IEnumerable<ModuleStateMaintenanceReceiptModule>? modules)
    {
        Source = string.IsNullOrWhiteSpace(source) ? null : source!.Trim();
        Modules = (modules ?? Array.Empty<ModuleStateMaintenanceReceiptModule>())
            .Where(static module => module is not null)
            .ToArray();
    }

    internal string? Source { get; }

    internal ModuleStateMaintenanceReceiptModule[] Modules { get; }
}
