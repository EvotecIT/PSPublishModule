using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

public sealed partial class InvokeModuleBuildCommand
{
    private static class LegacySegmentAdapter
    {
        public static IConfigurationSegment[] CollectFromSettings(ScriptBlock? settings)
        {
            if (settings is null) return Array.Empty<IConfigurationSegment>();

            var segments = new List<IConfigurationSegment>();
            foreach (var obj in settings.Invoke())
            {
                var baseObj = obj?.BaseObject;
                if (baseObj is null) continue;
                if (baseObj is PSObject pso) baseObj = pso.BaseObject;

                if (baseObj is IConfigurationSegment typed)
                {
                    segments.Add(typed);
                    continue;
                }

                if (baseObj is IDictionary dict)
                {
                    AddLegacySegmentDictionary(dict, segments);
                }
            }

            return segments.ToArray();
        }

        public static IConfigurationSegment[] CollectFromLegacyConfiguration(IDictionary configuration)
        {
            if (configuration is null) return Array.Empty<IConfigurationSegment>();

            var segments = new List<IConfigurationSegment>();

            var info = GetDictionary(configuration, "Information");
            var manifest = info is null ? null : GetDictionary(info, "Manifest");
            if (manifest is not null)
            {
                var tags = GetStringArray(manifest, "Tags") ?? GetNestedStringArray(manifest, "PrivateData", "PSData", "Tags");
                var iconUri = GetString(manifest, "IconUri") ?? GetNestedString(manifest, "PrivateData", "PSData", "IconUri");
                var projectUri = GetString(manifest, "ProjectUri") ?? GetNestedString(manifest, "PrivateData", "PSData", "ProjectUri");
                var prerelease = GetString(manifest, "Prerelease") ?? GetString(manifest, "PrereleaseTag");

                segments.Add(new ConfigurationManifestSegment
                {
                    Configuration = new ManifestConfiguration
                    {
                        ModuleVersion = GetString(manifest, "ModuleVersion") ?? string.Empty,
                        CompatiblePSEditions = GetStringArray(manifest, "CompatiblePSEditions") ?? Array.Empty<string>(),
                        Author = GetString(manifest, "Author") ?? string.Empty,
                        CompanyName = GetString(manifest, "CompanyName"),
                        Description = GetString(manifest, "Description"),
                        Tags = tags,
                        IconUri = iconUri,
                        ProjectUri = projectUri,
                        Prerelease = prerelease
                    }
                });

                AddRequiredModuleSegments(GetValue(manifest, "RequiredModules"), segments);
            }

            var steps = GetDictionary(configuration, "Steps");
            if (steps is not null)
            {
                var buildModule = GetDictionary(steps, "BuildModule");
                if (buildModule is not null)
                {
                    segments.Add(new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            LocalVersion = GetBool(buildModule, "LocalVersion"),
                            VersionedInstallStrategy = TryParseInstallationStrategy(GetString(buildModule, "VersionedInstallStrategy")),
                            VersionedInstallKeep = GetInt(buildModule, "VersionedInstallKeep")
                        }
                    });
                }

                var buildLibraries = GetDictionary(steps, "BuildLibraries");
                if (buildLibraries is not null)
                {
                    segments.Add(new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            Configuration = GetString(buildLibraries, "Configuration"),
                            Framework = GetStringArray(buildLibraries, "Framework"),
                            ProjectName = GetString(buildLibraries, "ProjectName"),
                            NETProjectPath = GetString(buildLibraries, "NETProjectPath")
                        }
                    });
                }
            }

            return segments.ToArray();
        }

        private static void AddLegacySegmentDictionary(IDictionary dict, List<IConfigurationSegment> output)
        {
            var type = GetString(dict, "Type");
            if (string.IsNullOrWhiteSpace(type)) return;

            if (string.Equals(type, "Manifest", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "Configuration");
                if (conf is null) return;

                output.Add(new ConfigurationManifestSegment
                {
                    Configuration = new ManifestConfiguration
                    {
                        ModuleVersion = GetString(conf, "ModuleVersion") ?? string.Empty,
                        CompatiblePSEditions = GetStringArray(conf, "CompatiblePSEditions") ?? Array.Empty<string>(),
                        Author = GetString(conf, "Author") ?? string.Empty,
                        CompanyName = GetString(conf, "CompanyName"),
                        Description = GetString(conf, "Description"),
                        Tags = GetStringArray(conf, "Tags"),
                        IconUri = GetString(conf, "IconUri"),
                        ProjectUri = GetString(conf, "ProjectUri"),
                        Prerelease = GetString(conf, "Prerelease") ?? GetString(conf, "PrereleaseTag")
                    }
                });

                return;
            }

            if (string.Equals(type, "Build", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "BuildModule");
                if (conf is null) return;

                output.Add(new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        LocalVersion = GetBool(conf, "LocalVersion"),
                        VersionedInstallStrategy = TryParseInstallationStrategy(GetString(conf, "VersionedInstallStrategy")),
                        VersionedInstallKeep = GetInt(conf, "VersionedInstallKeep")
                    }
                });
                return;
            }

            if (string.Equals(type, "BuildLibraries", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "BuildLibraries");
                if (conf is null) return;

                output.Add(new ConfigurationBuildLibrariesSegment
                {
                    BuildLibraries = new BuildLibrariesConfiguration
                    {
                        Configuration = GetString(conf, "Configuration"),
                        Framework = GetStringArray(conf, "Framework"),
                        ProjectName = GetString(conf, "ProjectName"),
                        NETProjectPath = GetString(conf, "NETProjectPath")
                    }
                });
                return;
            }

            if (string.Equals(type, "RequiredModule", StringComparison.OrdinalIgnoreCase))
            {
                AddRequiredModuleSegments(GetValue(dict, "Configuration"), output);
            }
        }

        private static void AddRequiredModuleSegments(object? value, List<IConfigurationSegment> output)
        {
            if (value is null) return;

            if (value is PSObject pso)
                value = pso.BaseObject;

            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                output.Add(new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = s.Trim() }
                });
                return;
            }

            if (value is IDictionary d)
            {
                var name = GetString(d, "ModuleName") ?? GetString(d, "Module");
                if (string.IsNullOrWhiteSpace(name)) return;

                output.Add(new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = name!.Trim(),
                        ModuleVersion = GetString(d, "ModuleVersion"),
                        RequiredVersion = GetString(d, "RequiredVersion"),
                        Guid = GetString(d, "Guid")
                    }
                });
                return;
            }

            if (value is IEnumerable e && value is not string)
            {
                foreach (var item in e)
                    AddRequiredModuleSegments(item, output);
            }
        }

        private static InstallationStrategy? TryParseInstallationStrategy(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<InstallationStrategy>(value!.Trim(), ignoreCase: true, out var parsed) ? parsed : null;
        }

        private static IDictionary? GetDictionary(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;
            return v as IDictionary;
        }

        private static object? GetValue(IDictionary dict, string key)
        {
            if (dict is null || string.IsNullOrWhiteSpace(key)) return null;

            if (dict.Contains(key))
            {
                try { return dict[key]; } catch { return null; }
            }

            foreach (DictionaryEntry entry in dict)
            {
                var k = entry.Key?.ToString();
                if (k is null) continue;
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            }

            return null;
        }

        private static string? GetString(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;
            return v?.ToString();
        }

        private static bool GetBool(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is bool b) return b;
            if (v is SwitchParameter sp) return sp.IsPresent;
            if (v is string s && bool.TryParse(s, out var parsed)) return parsed;
            return false;
        }

        private static int? GetInt(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            if (v is string s && int.TryParse(s, out var parsed)) return parsed;
            return null;
        }

        private static string[]? GetStringArray(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is null) return null;
            if (v is string s) return new[] { s };
            if (v is string[] sa) return sa;

            if (v is IEnumerable e)
            {
                var list = new List<string>();
                foreach (var item in e)
                {
                    if (item is null) continue;
                    if (item is PSObject pp) list.Add(pp.BaseObject?.ToString() ?? string.Empty);
                    else list.Add(item.ToString() ?? string.Empty);
                }
                return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            }

            return null;
        }

        private static string? GetNestedString(IDictionary root, string key1, string key2, string key3)
        {
            var d1 = GetDictionary(root, key1);
            var d2 = d1 is null ? null : GetDictionary(d1, key2);
            return d2 is null ? null : GetString(d2, key3);
        }

        private static string[]? GetNestedStringArray(IDictionary root, string key1, string key2, string key3)
        {
            var d1 = GetDictionary(root, key1);
            var d2 = d1 is null ? null : GetDictionary(d1, key2);
            return d2 is null ? null : GetStringArray(d2, key3);
        }
    }
}
