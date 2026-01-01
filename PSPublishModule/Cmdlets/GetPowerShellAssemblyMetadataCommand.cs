using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PSPublishModule;

/// <summary>
/// Gets the cmdlets and aliases in a .NET assembly by scanning for cmdlet-related attributes.
/// </summary>
/// <remarks>
/// <para>
/// This is typically used by module build tooling to determine which cmdlets and aliases should be exported
/// for binary modules (compiled cmdlets).
/// </para>
/// <para>
/// Under the hood it uses <c>System.Reflection.MetadataLoadContext</c> to inspect the assembly in isolation.
/// Make sure all dependencies of the target assembly are available next to it (or otherwise resolvable),
/// especially when running under Windows PowerShell 5.1.
/// </para>
/// </remarks>
/// <example>
/// <summary>Inspect a compiled PowerShell module assembly</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellAssemblyMetadata -Path '.\bin\Release\net8.0\MyModule.dll'</code>
/// <para>Returns discovered cmdlet and alias names based on PowerShell attributes.</para>
/// </example>
/// <example>
/// <summary>Inspect an assembly in a build artifact folder</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellAssemblyMetadata -Path 'C:\Artifacts\MyModule\Bin\MyModule.dll'</code>
/// <para>Useful when validating what will be exported before publishing.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "PowerShellAssemblyMetadata")]
public sealed class GetPowerShellAssemblyMetadataCommand : PSCmdlet
{
    /// <summary>The assembly to inspect.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Executes the assembly scan and returns cmdlets/aliases that should be exported.
    /// </summary>
    protected override void ProcessRecord()
    {
        var assemblyPath = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!File.Exists(assemblyPath))
        {
            WriteError(new ErrorRecord(
                new FileNotFoundException($"Assembly not found: {assemblyPath}"),
                "AssemblyNotFound",
                ErrorCategory.ObjectNotFound,
                assemblyPath));
            WriteObject(false);
            return;
        }

        WriteVerbose($"Loading assembly {assemblyPath}");

        var smaAssembly = typeof(PSObject).Assembly;
        var smaAssemblyPath = smaAssembly.Location;

        if (string.IsNullOrWhiteSpace(smaAssemblyPath))
        {
#if NETFRAMEWORK
            try
            {
                var codeBase = smaAssembly.CodeBase;
                if (!string.IsNullOrWhiteSpace(codeBase) && codeBase.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    smaAssemblyPath = Uri.UnescapeDataString(codeBase.Replace("file:///", string.Empty));
                }
            }
            catch
            {
                // ignored
            }
#endif
        }

        if (string.IsNullOrWhiteSpace(smaAssemblyPath))
        {
            WriteWarning("Could not determine the path to System.Management.Automation assembly.");
            WriteObject(false);
            return;
        }

        var assemblyDirectory = System.IO.Path.GetDirectoryName(assemblyPath) ?? string.Empty;
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

        var runtimeAssemblies = GetChildItemFullNames(runtimeDir, "*.dll", recurse: false);
        var assemblyFiles = string.IsNullOrWhiteSpace(assemblyDirectory)
            ? Array.Empty<string>()
            : GetChildItemFullNames(assemblyDirectory, "*.dll", recurse: true);

        var resolverCandidates = new List<string>(assemblyFiles.Count + runtimeAssemblies.Count + 1);
        resolverCandidates.AddRange(assemblyFiles);
        resolverCandidates.AddRange(runtimeAssemblies);
        resolverCandidates.Add(smaAssemblyPath);

        var uniquePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in resolverCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!uniquePaths.ContainsKey(name))
                uniquePaths[name] = candidate;
        }

        try
        {
#if NET8_0_OR_GREATER
            var resolver = new PathAssemblyResolver(uniquePaths.Values);
            using var context = new System.Reflection.MetadataLoadContext(
                resolver,
                coreAssemblyName: "mscorlib");
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            // Prefer FullName checks to avoid type identity issues across contexts
            const string cmdletTypeName = "System.Management.Automation.Cmdlet";
            const string pscmdletTypeName = "System.Management.Automation.PSCmdlet";
            const string cmdletAttributeName = "System.Management.Automation.CmdletAttribute";
            const string aliasAttributeName = "System.Management.Automation.AliasAttribute";

            var cmdletsToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliasesToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                types = assembly.GetExportedTypes();
            }

            foreach (var type in types)
            {
                CustomAttributeData? cmdletAttr = null;
                try
                {
                    cmdletAttr = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == cmdletAttributeName);
                }
                catch
                {
                    // ignore
                }

                if (cmdletAttr is null)
                {
                    // Fallback: walk base types by FullName to detect Cmdlet/PSCmdlet inheritance
                    var bt = type.BaseType;
                    bool isCmdlet = false;
                    while (bt != null)
                    {
                        if (bt.FullName == cmdletTypeName || bt.FullName == pscmdletTypeName) { isCmdlet = true; break; }
                        bt = bt.BaseType;
                    }
                    if (!isCmdlet) continue;
                }

                if (cmdletAttr is not null)
                {
                    var verb = cmdletAttr.ConstructorArguments.Count >= 2 ? cmdletAttr.ConstructorArguments[0].Value?.ToString() : null;
                    var noun = cmdletAttr.ConstructorArguments.Count >= 2 ? cmdletAttr.ConstructorArguments[1].Value?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(noun))
                        cmdletsToExport.Add($"{verb}-{noun}");
                }

                foreach (var aa in type.CustomAttributes.Where(a => a.AttributeType.FullName == aliasAttributeName))
                {
                    foreach (var arg in aa.ConstructorArguments)
                    {
                        if (arg.Value is IEnumerable<CustomAttributeTypedArgument> arr)
                        {
                                foreach (var v in arr)
                                {
                                    var s = v.Value?.ToString();
                                    if (!string.IsNullOrWhiteSpace(s)) aliasesToExport.Add(s!);
                                }
                            }
                            else if (arg.Value is string sa && !string.IsNullOrWhiteSpace(sa))
                            {
                            aliasesToExport.Add(sa);
                        }
                    }
                }
            }

            WriteObject(new PSObject(new
            {
                CmdletsToExport = cmdletsToExport.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
                AliasesToExport = aliasesToExport.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray()
            }));
#else
            // Legacy .NET Framework: use reflection-only load to avoid executing code.
            ResolveEventHandler? handler = null;
            handler = (_, args) =>
            {
                try
                {
                    var an = new AssemblyName(args.Name);
                    var dir = System.IO.Path.GetDirectoryName(assemblyPath);
                    if (string.IsNullOrWhiteSpace(dir)) return null;
                    var candidate = System.IO.Path.Combine(dir, an.Name + ".dll");
                    return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : null;
                }
                catch { return null; }
            };

            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += handler;
            try
            {
                var asm = Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                var cmdletsToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var aliasesToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var t in asm.GetTypes())
                {
                    foreach (var ca in CustomAttributeData.GetCustomAttributes(t))
                    {
                        if (ca.AttributeType.FullName == "System.Management.Automation.CmdletAttribute" && ca.ConstructorArguments.Count >= 2)
                        {
                            var verb = ca.ConstructorArguments[0].Value?.ToString();
                            var noun = ca.ConstructorArguments[1].Value?.ToString();
                            if (!string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(noun))
                                cmdletsToExport.Add($"{verb}-{noun}");
                        }
                        if (ca.AttributeType.FullName == "System.Management.Automation.AliasAttribute")
                        {
                            foreach (var arg in ca.ConstructorArguments)
                            {
                                if (arg.Value is IEnumerable<CustomAttributeTypedArgument> arr)
                                {
                                    foreach (var v in arr)
                                    {
                                        var s = v.Value?.ToString();
                                        if (!string.IsNullOrWhiteSpace(s)) aliasesToExport.Add(s!);
                                    }
                                }
                                else if (arg.Value is string sa && !string.IsNullOrWhiteSpace(sa))
                                {
                                    aliasesToExport.Add(sa);
                                }
                            }
                        }
                    }
                }

                WriteObject(new PSObject(new
                {
                    CmdletsToExport = cmdletsToExport.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
                    AliasesToExport = aliasesToExport.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray()
                }));
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= handler;
            }
#endif
        }
        catch (Exception ex)
        {
            if (ex.Message.IndexOf("has already been loaded into this MetadataLoadContext", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WriteObject(new PSObject(new { CmdletsToExport = Array.Empty<string>(), AliasesToExport = Array.Empty<string>() }));
                return;
            }

            WriteWarning($"Can't load assembly {assemblyPath}. Error: {ex.Message}");
            WriteObject(false);
        }
    }

    private IReadOnlyList<string> GetChildItemFullNames(string path, string filter, bool recurse)
    {
        var script = recurse
            ? "param($p,$f) Get-ChildItem -Path $p -Filter $f -Recurse -File | Select-Object -ExpandProperty FullName"
            : "param($p,$f) Get-ChildItem -Path $p -Filter $f -File | Select-Object -ExpandProperty FullName";

        var results = InvokeCommand.InvokeScript(script, new object[] { path, filter });
        return results
            .Select(r => r?.BaseObject?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray()!;
    }
}
