using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>Infers scan-time availability for legacy inventories that predate structured path provenance.</summary>
internal static class ModuleStateInventoryPathAvailability
{
    internal static bool WasAvailable(string path, IEnumerable<ModuleStateInventoryDiagnostic> diagnostics)
        => !(diagnostics ?? Array.Empty<ModuleStateInventoryDiagnostic>()).Any(diagnostic =>
            IsUnavailableDiagnostic(diagnostic.Code) &&
            ModuleStatePathIdentity.Equals(path, diagnostic.Path));

    private static bool IsUnavailableDiagnostic(string? code)
        => string.Equals(code, "ModuleState.InventoryPathMissing", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(code, "ModuleState.InventoryPathInaccessible", StringComparison.OrdinalIgnoreCase);
}
