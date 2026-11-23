using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PowerForge.ModuleLoader.V1;

internal static class TypeAcceleratorForwardedTypes
{
    internal static IEnumerable<Type> Read(Assembly facade, Action<string> warn)
    {
        // .NET Framework has no GetForwardedTypes. An opted-in facade identifies one forwarded type per target.
        var anchors = facade.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static item => item.Key == "PowerForge.TypeForwarderAnchor")
            .Select(static item => item.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in anchors)
        {
            Type[] forwarded;
            try
            {
                var anchor = facade.GetType(name!, false, false);
                if (anchor is null || ReferenceEquals(anchor.Assembly, facade))
                {
                    warn($"Type forwarder '{name}' could not be resolved for assembly '{facade.GetName().Name}'.");
                    continue;
                }

                forwarded = anchor.Assembly.GetExportedTypes()
                    .Where(type => ReferenceEquals(facade.GetType(type.FullName!, false, false), type))
                    .ToArray();
            }
            catch (Exception ex)
            {
                warn($"Could not enumerate forwarders from assembly '{facade.GetName().Name}' through '{name}': {ex.Message}");
                continue;
            }

            foreach (var type in forwarded)
                yield return type;
        }
    }
}
