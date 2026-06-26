using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateFamilyCatalog
{
    private static readonly Dictionary<string, ModuleStateFamilyPolicy> Policies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MicrosoftGraph"] = new ModuleStateFamilyPolicy(
            "MicrosoftGraph",
            new[] { "Microsoft.Graph" },
            modulePrefixes: new[] { "Microsoft.Graph." }),
        ["Graph"] = new ModuleStateFamilyPolicy(
            "MicrosoftGraph",
            new[] { "Microsoft.Graph" },
            modulePrefixes: new[] { "Microsoft.Graph." }),
        ["Az"] = new ModuleStateFamilyPolicy(
            "Az",
            new[]
            {
                "Az.Accounts",
                "Az.Resources",
                "Az.KeyVault",
                "Az.Storage"
            },
            ModuleStateFamilyCoherenceRule.ObserveOnly),
        ["ExchangeOnline"] = new ModuleStateFamilyPolicy(
            "ExchangeOnline",
            new[] { "ExchangeOnlineManagement" },
            ModuleStateFamilyCoherenceRule.ObserveOnly),
        ["Teams"] = new ModuleStateFamilyPolicy(
            "Teams",
            new[] { "MicrosoftTeams" },
            ModuleStateFamilyCoherenceRule.ObserveOnly)
    };

    internal ModuleStateFamilyPolicy[] Resolve(IEnumerable<string>? familyNames)
    {
        var names = (familyNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
            return Array.Empty<ModuleStateFamilyPolicy>();

        var policies = new List<ModuleStateFamilyPolicy>();
        foreach (var name in names)
        {
            if (!Policies.TryGetValue(name, out var policy))
                throw new ArgumentException($"Unknown ModuleState family '{name}'. Known families: {string.Join(", ", GetKnownFamilyNames())}.", nameof(familyNames));

            if (policies.Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            policies.Add(policy);
        }

        return policies.ToArray();
    }

    internal string[] GetKnownFamilyNames()
        => Policies.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
