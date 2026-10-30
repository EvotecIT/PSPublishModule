            sharedNames = new HashSet<string>(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase);
            if (sharedNames.Count > 0) {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    Type policy = assembly.GetType("PowerForge.Generated.SharedAssemblyPolicyV3", false);
                    if (policy == null) continue;
                    resolveShared = (Func<AssemblyName, string[], Assembly>)Delegate.CreateDelegate(typeof(Func<AssemblyName, string[], Assembly>), policy.GetMethod("Resolve", new[] { typeof(AssemblyName), typeof(string[]) }));
                    break;
                }
            }
