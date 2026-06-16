using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleDependencyCommandHelpers
{
    public static string? GetProperty(PSObject obj, string name)
        => obj.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.ToString();

    public static string ResolveEmbeddedManifestPath(PSCmdlet cmdlet, string moduleBase)
    {
        var manifestPath = Directory.GetFiles(moduleBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var internalsPath = ReadDeliveryInternalsPath(cmdlet, manifestPath!);
            if (!string.IsNullOrWhiteSpace(internalsPath))
            {
                var modulesRoot = EmbeddedModuleDependencyService.ResolveInternalsModulesRoot(moduleBase, internalsPath!);
                return EmbeddedModuleDependencyService.ResolveManifestPath(modulesRoot);
            }
        }

        return EmbeddedModuleDependencyService.FindManifestForModuleBase(moduleBase);
    }

    private static string? ReadDeliveryInternalsPath(PSCmdlet cmdlet, string manifestPath)
    {
        try
        {
            var value = cmdlet.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Delivery.InternalsPath")
                .Invoke(manifestPath)
                .FirstOrDefault();
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
