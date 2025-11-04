// ReSharper disable All
#nullable disable
using System;
using System.Linq;
using System.Management.Automation;
using System.Collections.ObjectModel;

namespace PSMaintenance;

internal sealed class ModuleResolver
{
    private readonly PSCmdlet _cmdlet;
    public ModuleResolver(PSCmdlet cmdlet) => _cmdlet = cmdlet;

    public PSObject Resolve(string name, PSObject module, Version requiredVersion)
    {
        if (module != null)
        {
            var modName = module.Properties["Name"]?.Value?.ToString();
            var modVersion = module.Properties["Version"]?.Value?.ToString();
            if (requiredVersion != null && !string.Equals(modVersion, requiredVersion?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var script = _cmdlet.InvokeCommand.NewScriptBlock("$m = Get-Module -ListAvailable -Name $args[0] | Where-Object { $_.Version -eq $args[1] } | Sort-Object Version -Descending | Select-Object -First 1; $m");
                var result = script.Invoke(modName, requiredVersion).FirstOrDefault() as PSObject;
                if (result == null)
                    throw new ItemNotFoundException($"Module '{modName}' with version {requiredVersion} not found.");
                return result;
            }
            return module;
        }

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Specify -Name or provide -Module.");

        if (requiredVersion != null)
        {
            var script = _cmdlet.InvokeCommand.NewScriptBlock("$m = Get-Module -ListAvailable -Name $args[0] | Where-Object { $_.Version -eq $args[1] } | Sort-Object Version -Descending | Select-Object -First 1; $m");
            var result = script.Invoke(name, requiredVersion).FirstOrDefault() as PSObject;
            if (result == null)
                throw new ItemNotFoundException($"Module '{name}' with version {requiredVersion} not found.");
            return result;
        }

        var script2 = _cmdlet.InvokeCommand.NewScriptBlock("$m = Get-Module -ListAvailable -Name $args[0] | Sort-Object Version -Descending | Select-Object -First 1; $m");
        var result2 = script2.Invoke(name).FirstOrDefault() as PSObject;
        if (result2 == null)
            throw new ItemNotFoundException($"Module '{name}' not found.");
        return result2;
    }

    public PSObject ReadManifest(string manifestPath)
    {
        var sb = _cmdlet.InvokeCommand.NewScriptBlock("Test-ModuleManifest -Path $args[0]");
        var result = sb.Invoke(manifestPath).FirstOrDefault() as PSObject;
        return result;
    }
}
