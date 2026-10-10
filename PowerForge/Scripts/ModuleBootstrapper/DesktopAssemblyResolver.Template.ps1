$UnregisterPowerForgeDesktopAssemblyResolver = $null
$PowerForgeDesktopAssemblyRoots = @()
if ($PSEdition -ne 'Core') {
    if ($null -ne $ResolvePowerForgeModuleAssembly -and $LibraryFileNames.Count -gt 0) {
        foreach ($PowerForgeDesktopLibraryFileName in $LibraryFileNames) {
            try {
                $PowerForgeDesktopResolvedModule = & $ResolvePowerForgeModuleAssembly -LibraryFileName $PowerForgeDesktopLibraryFileName
            } catch {
                Write-Verbose "Skipping Desktop resolver root for '$PowerForgeDesktopLibraryFileName'. $($_.Exception.Message)"
                continue
            }
            if ($PowerForgeDesktopResolvedModule.Directory -notin $PowerForgeDesktopAssemblyRoots) {
                $PowerForgeDesktopAssemblyRoots += $PowerForgeDesktopResolvedModule.Directory
            }
        }
    } elseif ($LibFolder -or $Root) {
        $PowerForgeDesktopAssemblyRoots += if ($LibFolder) {
            [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder))
        } else {
            [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, 'Lib'))
        }
    }
}
if ($PSEdition -ne 'Core' -and $PowerForgeDesktopAssemblyRoots.Count -gt 0) {
    $PowerForgeDesktopAssemblyRootPrefixes = @($PowerForgeDesktopAssemblyRoots | ForEach-Object {
        $PowerForgeDesktopAssemblyRootPrefix = [IO.Path]::GetFullPath($_)
        if (-not $PowerForgeDesktopAssemblyRootPrefix.EndsWith([IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
            $PowerForgeDesktopAssemblyRootPrefix += [IO.Path]::DirectorySeparatorChar
        }
        $PowerForgeDesktopAssemblyRootPrefix
    })
    if ($PowerForgeDesktopAssemblyRootPrefixes.Count -eq 0) {
        $PowerForgeDesktopAssemblyRoots = @()
    }
    # A CLR callback must not execute a PowerShell scriptblock: AssemblyResolve
    # also runs on worker threads that have no runspace. Compile this BCL-only
    # implementation with the host's built-in Add-Type, once per process.
    $PowerForgeDesktopResolverTypeLock = [string]::Intern('PowerForge.Generated.DesktopAssemblyResolverV1')
    [Threading.Monitor]::Enter($PowerForgeDesktopResolverTypeLock)
    try {
      if (-not ('PowerForge.Generated.DesktopAssemblyResolverV1' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace PowerForge.Generated {
    public sealed class DesktopAssemblyResolverV1 {
        private readonly string[] roots;
        private readonly object lifetime = new object();
        private volatile bool bootstrapActive = true;
        private int registered;
        [ThreadStatic] private static HashSet<DesktopAssemblyResolverV1> resolving;

        public DesktopAssemblyResolverV1(string[] assemblyRoots) {
            roots = new string[assemblyRoots.Length];
            for (int i = 0; i < roots.Length; i++)
                roots[i] = Path.GetFullPath(assemblyRoots[i]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        public bool BootstrapActive { get { return bootstrapActive; } set { bootstrapActive = value; } }
        public bool Registered { get { return Thread.VolatileRead(ref registered) != 0; } }
        public ResolveEventHandler Handler { get { return Resolve; } }

        public void Register() {
            lock (lifetime) {
                if (Registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                Interlocked.Exchange(ref registered, 1);
            }
        }
        public void Unregister() {
            lock (lifetime) {
                AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
                Interlocked.Exchange(ref registered, 0);
            }
        }
        private bool IsModuleAssembly(Assembly assembly) {
            if (assembly == null || assembly.IsDynamic || String.IsNullOrWhiteSpace(assembly.Location)) return false;
            string path = Path.GetFullPath(assembly.Location);
            foreach (string root in roots)
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private bool IsDeclaredReference(string fullName) {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (!IsModuleAssembly(assembly)) continue;
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                    if (String.Equals(reference.FullName, fullName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        private static bool SameIdentity(AssemblyName requested, AssemblyName candidate) {
            if (!String.Equals(requested.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(requested.CultureName ?? "", candidate.CultureName ?? "", StringComparison.OrdinalIgnoreCase) ||
                (requested.Version != null && candidate.Version < requested.Version)) return false;
            byte[] expected = requested.GetPublicKeyToken() ?? new byte[0];
            byte[] actual = candidate.GetPublicKeyToken() ?? new byte[0];
            if (expected.Length != actual.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) return false;
            return true;
        }
        private Assembly Resolve(object sender, ResolveEventArgs args) {
            if (!Registered || args == null) return null;
            if (resolving == null) resolving = new HashSet<DesktopAssemblyResolverV1>();
            if (!resolving.Add(this)) return null;
            try {
                // Null callers cannot be attributed within an AppDomain. Accept
                // only exact references declared by this module after bootstrap;
                // another null caller requesting that same identity can share it.
                if (args.RequestingAssembly == null || args.RequestingAssembly.IsDynamic ||
                    String.IsNullOrWhiteSpace(args.RequestingAssembly.Location)) {
                    if (!BootstrapActive && !IsDeclaredReference(args.Name)) return null;
                } else if (!IsModuleAssembly(args.RequestingAssembly)) return null;

                AssemblyName requested = new AssemblyName(args.Name);
                string name = requested.Name;
                if (String.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                foreach (string root in roots) {
                    string candidate = Path.GetFullPath(Path.Combine(root, name + ".dll"));
                    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) continue;
                    if (SameIdentity(requested, AssemblyName.GetAssemblyName(candidate))) return Assembly.LoadFrom(candidate);
                }
                return null;
            } catch { return null; }
            finally { resolving.Remove(this); }
        }
    }
}
'@ -ErrorAction Stop
      }
    } finally {
        [Threading.Monitor]::Exit($PowerForgeDesktopResolverTypeLock)
    }
    $PowerForgeDesktopAssemblyResolverState = [PowerForge.Generated.DesktopAssemblyResolverV1]::new([string[]]$PowerForgeDesktopAssemblyRoots)
    $PowerForgeDesktopAssemblyResolver = $PowerForgeDesktopAssemblyResolverState.Handler
    $PowerForgeDesktopAssemblyResolverState.Register()
    $UnregisterPowerForgeDesktopAssemblyResolver = {
        $PowerForgeDesktopAssemblyResolverState.Unregister()
    }.GetNewClosure()

    $PowerForgePreviousOnRemove = $ExecutionContext.SessionState.Module.OnRemove
    $ExecutionContext.SessionState.Module.OnRemove = {
        try {
            if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                & $UnregisterPowerForgeDesktopAssemblyResolver
            }
        } finally {
            if ($null -ne $PowerForgePreviousOnRemove) {
                & $PowerForgePreviousOnRemove @args
            }
        }
    }.GetNewClosure()
}
