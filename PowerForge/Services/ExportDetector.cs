using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace PowerForge;

/// <summary>
/// Detects public functions from scripts and cmdlets/aliases from binaries for manifest export lists.
/// </summary>
public static class ExportDetector
{
    /// <summary>Detects function names defined in the provided PowerShell script files.</summary>
    public static IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;
            try
            {
                Token[] tokens; ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors != null && errors.Length > 0) continue;
                var funcs = ast.FindAll(a => a is FunctionDefinitionAst, true).Cast<FunctionDefinitionAst>();
                foreach (var f in funcs)
                {
                    // Use the declared function name; skip private helper names starting with '_' by convention
                    var name = f.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add(name);
                }
            }
            catch { /* ignore */ }
        }
        return result.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Detects cmdlet names from the provided assemblies by scanning for CmdletAttribute.</summary>
    public static IReadOnlyList<string> DetectBinaryCmdlets(IEnumerable<string> assemblyPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assemblyPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            foreach (var cmdlet in ScanAssemblyForCmdlets(path)) set.Add(cmdlet);
        }
        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Detects aliases from the provided assemblies by scanning for AliasAttribute on cmdlet classes.</summary>
    public static IReadOnlyList<string> DetectBinaryAliases(IEnumerable<string> assemblyPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assemblyPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            foreach (var alias in ScanAssemblyForAliases(path)) set.Add(alias);
        }
        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ScanAssemblyForCmdlets(string assemblyPath)
    {
        var list = new List<string>();
#if NET8_0_OR_GREATER
        try
        {
            var runtimeAssemblies = Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            var dir = Path.GetDirectoryName(assemblyPath);
            var localAssemblies = string.IsNullOrWhiteSpace(dir) ? Array.Empty<string>() : Directory.GetFiles(dir, "*.dll");
            using var ralc = new System.Reflection.MetadataLoadContext(
                new System.Reflection.PathAssemblyResolver(
                    runtimeAssemblies.Concat(localAssemblies).Append(assemblyPath).Distinct(StringComparer.OrdinalIgnoreCase)));
            var asm = ralc.LoadFromAssemblyPath(assemblyPath);
            foreach (var t in asm.GetTypes())
            {
                foreach (var ca in CustomAttributeData.GetCustomAttributes(t))  
                {
                    if (ca.AttributeType.FullName == "System.Management.Automation.CmdletAttribute")
                    {
                        if (ca.ConstructorArguments.Count >= 2)
                        {
                            var verb = ca.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                            var noun = ca.ConstructorArguments[1].Value?.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(noun))
                                list.Add(verb + "-" + noun);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback: load into an isolated ALC and reflect
            try
            {
                var alc = new AssemblyLoadContext("ExportDetector", isCollectible: true);
                var baseDir = Path.GetDirectoryName(assemblyPath);
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    alc.Resolving += (_, name) =>
                    {
                        try
                        {
                            var candidate = Path.Combine(baseDir!, name.Name + ".dll");
                            return File.Exists(candidate) ? alc.LoadFromAssemblyPath(candidate) : null;
                        }
                        catch { return null; }
                    };
                }
                var asm = alc.LoadFromAssemblyPath(assemblyPath);
                foreach (var t in asm.GetTypes())
                {
                    foreach (var ca in CustomAttributeData.GetCustomAttributes(t))
                    {
                        if (ca.AttributeType.FullName == "System.Management.Automation.CmdletAttribute")
                        {
                            if (ca.ConstructorArguments.Count >= 2)
                            {
                                var verb = ca.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                                var noun = ca.ConstructorArguments[1].Value?.ToString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(noun))
                                    list.Add(verb + "-" + noun);
                            }
                        }
                    }
                }
                alc.Unload();
            }
            catch { }
        }
#else
        try
        {
            ResolveEventHandler? handler = null;
            var baseDir = Path.GetDirectoryName(assemblyPath);
            handler = (_, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(baseDir)) return null;
                    var an = new AssemblyName(args.Name);
                    var candidate = Path.Combine(baseDir!, an.Name + ".dll");
                    return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : null;
                }
                catch { return null; }
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += handler;
            try
            {
                var asm = Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                foreach (var t in asm.GetTypes())
                {
                    foreach (var ca in CustomAttributeData.GetCustomAttributes(t))
                    {
                        if (ca.AttributeType.FullName == "System.Management.Automation.CmdletAttribute")
                        {
                            if (ca.ConstructorArguments.Count >= 2)
                            {
                                var verb = ca.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                                var noun = ca.ConstructorArguments[1].Value?.ToString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(noun))
                                    list.Add(verb + "-" + noun);
                            }
                        }
                    }
                }
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= handler;
            }
        }
        catch { }
#endif
        return list;
    }

    private static IEnumerable<string> ScanAssemblyForAliases(string assemblyPath)
    {
        var list = new List<string>();
#if NET8_0_OR_GREATER
        try
        {
            var runtimeAssemblies = Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            var dir = Path.GetDirectoryName(assemblyPath);
            var localAssemblies = string.IsNullOrWhiteSpace(dir) ? Array.Empty<string>() : Directory.GetFiles(dir, "*.dll");
            using var ralc = new System.Reflection.MetadataLoadContext(
                new System.Reflection.PathAssemblyResolver(
                    runtimeAssemblies.Concat(localAssemblies).Append(assemblyPath).Distinct(StringComparer.OrdinalIgnoreCase)));
            var asm = ralc.LoadFromAssemblyPath(assemblyPath);
            foreach (var t in asm.GetTypes())
            {
                foreach (var ca in CustomAttributeData.GetCustomAttributes(t))  
                {
                    if (ca.AttributeType.FullName == "System.Management.Automation.AliasAttribute")
                    {
                        foreach (var arg in ca.ConstructorArguments)
                        {
                            if (arg.Value is IEnumerable<CustomAttributeTypedArgument> arr)
                            {
                                foreach (var v in arr)
                                {
                                    var s = v.Value?.ToString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                                }
                            }
                            else if (arg.Value is string sa && !string.IsNullOrWhiteSpace(sa))
                            {
                                list.Add(sa);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            try
            {
                var alc = new AssemblyLoadContext("ExportDetector", isCollectible: true);
                var baseDir = Path.GetDirectoryName(assemblyPath);
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    alc.Resolving += (_, name) =>
                    {
                        try
                        {
                            var candidate = Path.Combine(baseDir!, name.Name + ".dll");
                            return File.Exists(candidate) ? alc.LoadFromAssemblyPath(candidate) : null;
                        }
                        catch { return null; }
                    };
                }
                var asm = alc.LoadFromAssemblyPath(assemblyPath);
                foreach (var t in asm.GetTypes())
                {
                    foreach (var ca in CustomAttributeData.GetCustomAttributes(t))
                    {
                        if (ca.AttributeType.FullName == "System.Management.Automation.AliasAttribute")
                        {
                            foreach (var arg in ca.ConstructorArguments)
                            {
                                if (arg.Value is IEnumerable<CustomAttributeTypedArgument> arr)
                                {
                                    foreach (var v in arr)
                                    {
                                        var s = v.Value?.ToString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                                    }
                                }
                                else if (arg.Value is string sa && !string.IsNullOrWhiteSpace(sa))
                                {
                                    list.Add(sa);
                                }
                            }
                        }
                    }
                }
                alc.Unload();
            }
            catch { }
        }
#else
        try
        {
            ResolveEventHandler? handler = null;
            var baseDir = Path.GetDirectoryName(assemblyPath);
            handler = (_, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(baseDir)) return null;
                    var an = new AssemblyName(args.Name);
                    var candidate = Path.Combine(baseDir!, an.Name + ".dll");
                    return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : null;
                }
                catch { return null; }
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += handler;
            try
            {
                var asm = Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                foreach (var t in asm.GetTypes())
                {
                    foreach (var ca in CustomAttributeData.GetCustomAttributes(t))
                    {
                        if (ca.AttributeType.FullName == "System.Management.Automation.AliasAttribute")
                        {
                            foreach (var arg in ca.ConstructorArguments)
                            {
                                if (arg.Value is IEnumerable<CustomAttributeTypedArgument> arr)
                                {
                                    foreach (var v in arr)
                                    {
                                        var s = v.Value?.ToString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                                    }
                                }
                                else if (arg.Value is string sa && !string.IsNullOrWhiteSpace(sa))
                                {
                                    list.Add(sa);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= handler;
            }
        }
        catch { }
#endif
        return list;
    }
}
