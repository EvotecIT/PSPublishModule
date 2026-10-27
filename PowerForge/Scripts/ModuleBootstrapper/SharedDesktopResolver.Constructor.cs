            sharedNames = new HashSet<string>(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase);
            if (sharedNames.Count > 0) {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    Type policy = assembly.GetType("PowerForge.Generated.SharedAssemblyPolicyV2", false);
                    if (policy == null) continue;
                    resolveShared = (Func<string, string[], Assembly>)Delegate.CreateDelegate(typeof(Func<string, string[], Assembly>), policy.GetMethod("Resolve"));
                    break;
                }
            }
