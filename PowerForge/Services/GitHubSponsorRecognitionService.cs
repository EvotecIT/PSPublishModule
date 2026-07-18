namespace PowerForge;

/// <summary>
/// Applies manual entries, presentation overrides, and opt-in recognition tiers to GitHub Sponsors data.
/// </summary>
public sealed class GitHubSponsorRecognitionService
{
    private static readonly GitHubSponsorRecognitionTierSpec[] DefaultTiers =
    {
        new() { Key = "Principal", Heading = "Principal Sponsors", MinimumMonthlyDollars = 1000, Order = 10, AvatarSize = 112 },
        new() { Key = "Platinum", Heading = "Platinum Sponsors", MinimumMonthlyDollars = 100, Order = 20, AvatarSize = 96 },
        new() { Key = "Gold", Heading = "Gold Sponsors", MinimumMonthlyDollars = 30, Order = 30, AvatarSize = 80 },
        new() { Key = "Silver", Heading = "Silver Sponsors", MinimumMonthlyDollars = 10, Order = 40, AvatarSize = 72 },
        new() { Key = "Bronze", Heading = "Bronze Sponsors", MinimumMonthlyDollars = 5, Order = 50, AvatarSize = 64 },
        new() { Key = "Sponsors", Heading = "Sponsors", MinimumMonthlyDollars = null, Order = 60, AvatarSize = 64 }
    };

    /// <summary>
    /// Prepares sponsor records for rendering.
    /// </summary>
    /// <param name="source">Public sponsors returned by GitHub.</param>
    /// <param name="spec">Sponsors content specification.</param>
    /// <returns>Normalized recognition result.</returns>
    public GitHubSponsorRecognitionResult Prepare(IEnumerable<GitHubSponsorRecord> source, GitHubSponsorsContentSpec spec)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return Prepare(source.Select(item => new GitHubSponsorSourceRecord { Sponsor = item }), spec);
    }

    /// <summary>
    /// Prepares sponsor records using private in-process funding data when tier recognition is enabled.
    /// </summary>
    internal GitHubSponsorRecognitionResult Prepare(IEnumerable<GitHubSponsorSourceRecord> source, GitHubSponsorsContentSpec spec)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var records = source.Select(Clone).ToList();
        AddManualEntries(records, spec.ManualEntries);
        ApplyOverrides(records, spec.Overrides);
        records.RemoveAll(record => record.Excluded);

        var recognition = spec.TierRecognition ?? new GitHubSponsorTierRecognitionSpec();
        var tiers = recognition.Enabled ? NormalizeTiers(recognition) : Array.Empty<GitHubSponsorRecognitionTierSpec>();
        if (recognition.Enabled)
            AssignRecognitionTiers(records, tiers, NormalizeRequired(recognition.UnmappedTierKey, "Unmapped recognition tier key is required."));
        else
            records.ForEach(record => record.Value.RecognitionTierKey = null);

        return new GitHubSponsorRecognitionResult
        {
            TierRecognitionEnabled = recognition.Enabled,
            Tiers = tiers,
            Sponsors = records
                .Select(record => record.Value)
                .OrderBy(record => record.Status)
                .ThenBy(record => TierOrder(record.RecognitionTierKey, tiers))
                .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void AddManualEntries(List<MutableSponsor> records, GitHubManualSponsorSpec[]? manualEntries)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            existing.Add(record.Value.Key);
            if (!string.IsNullOrWhiteSpace(record.Value.Login)) existing.Add(record.Value.Login!);
        }
        var manualIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manualEntries ?? Array.Empty<GitHubManualSponsorSpec>())
        {
            var key = NormalizeRequired(entry.Key, "Manual sponsor key is required.");
            var login = NormalizeOptional(entry.Login);
            var identities = new[] { key, login }.Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray();
            foreach (var identity in identities)
            {
                if (!manualIdentities.Add(identity))
                    throw new InvalidOperationException($"Manual sponsor identity '{identity}' is configured more than once.");
                if (existing.Contains(identity))
                    throw new InvalidOperationException($"Manual sponsor identity '{identity}' duplicates an existing sponsor. Use an override instead.");
            }

            var displayName = NormalizeOptional(entry.DisplayName) ?? login ?? key;
            records.Add(new MutableSponsor(new GitHubSponsorRecord
            {
                Key = key,
                Login = login,
                DisplayName = displayName,
                ProfileUrl = NormalizeOptional(entry.ProfileUrl) ?? (login is null ? null : $"https://github.com/{login}"),
                AvatarUrl = NormalizeOptional(entry.AvatarUrl) ?? (login is null ? null : $"https://github.com/{login}.png"),
                Status = entry.Former ? GitHubSponsorStatus.Former : GitHubSponsorStatus.Current,
                EntityType = GitHubSponsorEntityType.Manual,
                RecognitionTierKey = NormalizeOptional(entry.RecognitionTierKey)
            }, explicitTier: !string.IsNullOrWhiteSpace(entry.RecognitionTierKey)));
            foreach (var identity in identities) existing.Add(identity);
        }
    }

    private static void ApplyOverrides(List<MutableSponsor> records, GitHubSponsorOverrideSpec[]? overrides)
    {
        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides ?? Array.Empty<GitHubSponsorOverrideSpec>())
        {
            var login = NormalizeRequired(item.Login, "Sponsor override login is required.");
            if (!configured.Add(login))
                throw new InvalidOperationException($"Sponsor override '{login}' is configured more than once.");

            var record = records.FirstOrDefault(candidate =>
                string.Equals(candidate.Value.Key, login, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Value.Login, login, StringComparison.OrdinalIgnoreCase));
            if (record is null)
                continue;

            record.Excluded = item.Exclude;
            record.Value.DisplayName = NormalizeOptional(item.DisplayName) ?? record.Value.DisplayName;
            record.Value.ProfileUrl = NormalizeOptional(item.ProfileUrl) ?? record.Value.ProfileUrl;
            record.Value.AvatarUrl = NormalizeOptional(item.AvatarUrl) ?? record.Value.AvatarUrl;
            if (!string.IsNullOrWhiteSpace(item.RecognitionTierKey))
            {
                record.Value.RecognitionTierKey = item.RecognitionTierKey!.Trim();
                record.ExplicitTier = true;
            }
        }
    }

    private static GitHubSponsorRecognitionTierSpec[] NormalizeTiers(GitHubSponsorTierRecognitionSpec recognition)
    {
        var configured = recognition.Tiers ?? Array.Empty<GitHubSponsorRecognitionTierSpec>();
        var source = configured.Length == 0 && recognition.UseDefaultTiers ? DefaultTiers : configured;
        if (source.Length == 0)
            throw new InvalidOperationException("Tier recognition is enabled but no recognition tiers are configured.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tiers = source.Select(item =>
        {
            var key = NormalizeRequired(item.Key, "Recognition tier key is required.");
            if (!keys.Add(key))
                throw new InvalidOperationException($"Recognition tier key '{key}' is configured more than once.");
            if (item.MinimumMonthlyDollars is < 0)
                throw new InvalidOperationException($"Recognition tier '{key}' cannot have a negative minimum monthly amount.");

            return new GitHubSponsorRecognitionTierSpec
            {
                Key = key,
                Heading = NormalizeOptional(item.Heading) ?? key,
                MinimumMonthlyDollars = item.MinimumMonthlyDollars,
                Order = item.Order,
                AvatarSize = Math.Max(24, Math.Min(256, item.AvatarSize))
            };
        }).OrderBy(item => item.Order).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray();

        var fallback = NormalizeRequired(recognition.UnmappedTierKey, "Unmapped recognition tier key is required.");
        if (!keys.Contains(fallback))
            throw new InvalidOperationException($"Unmapped recognition tier '{fallback}' is not present in the configured tiers.");
        return tiers;
    }

    private static void AssignRecognitionTiers(List<MutableSponsor> records, GitHubSponsorRecognitionTierSpec[] tiers, string unmappedTierKey)
    {
        var fallback = tiers.First(tier => string.Equals(tier.Key, unmappedTierKey.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
        var pricedTiers = tiers
            .Where(tier => tier.MinimumMonthlyDollars is not null)
            .OrderByDescending(tier => tier.MinimumMonthlyDollars)
            .ThenBy(tier => tier.Order)
            .ToArray();

        foreach (var record in records)
        {
            if (record.Value.Status == GitHubSponsorStatus.Former)
            {
                record.Value.RecognitionTierKey = null;
                continue;
            }

            if (record.ExplicitTier)
            {
                var explicitKey = record.Value.RecognitionTierKey ?? string.Empty;
                var configured = tiers.FirstOrDefault(tier => string.Equals(tier.Key, explicitKey, StringComparison.OrdinalIgnoreCase));
                if (configured is null)
                    throw new InvalidOperationException($"Sponsor '{record.Value.Key}' references unknown recognition tier '{explicitKey}'.");
                record.Value.RecognitionTierKey = configured.Key;
                continue;
            }

            var amount = record.FundingTierMonthlyDollars;
            record.Value.RecognitionTierKey = amount is null
                ? fallback
                : pricedTiers.FirstOrDefault(tier => amount.Value >= tier.MinimumMonthlyDollars!.Value)?.Key ?? fallback;
        }
    }

    private static int TierOrder(string? key, GitHubSponsorRecognitionTierSpec[] tiers)
        => tiers.FirstOrDefault(tier => string.Equals(tier.Key, key, StringComparison.OrdinalIgnoreCase))?.Order ?? int.MaxValue;

    private static MutableSponsor Clone(GitHubSponsorSourceRecord source)
        => new(new GitHubSponsorRecord
        {
            Key = source.Sponsor.Key,
            Login = source.Sponsor.Login,
            DisplayName = source.Sponsor.DisplayName,
            ProfileUrl = source.Sponsor.ProfileUrl,
            AvatarUrl = source.Sponsor.AvatarUrl,
            Status = source.Sponsor.Status,
            EntityType = source.Sponsor.EntityType,
            RecognitionTierKey = source.Sponsor.RecognitionTierKey
        }, explicitTier: !string.IsNullOrWhiteSpace(source.Sponsor.RecognitionTierKey), source.FundingTierMonthlyDollars);

    private static string NormalizeRequired(string? value, string message)
        => NormalizeOptional(value) ?? throw new InvalidOperationException(message);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private sealed class MutableSponsor
    {
        internal MutableSponsor(GitHubSponsorRecord value, bool explicitTier, int? fundingTierMonthlyDollars = null)
        {
            Value = value;
            ExplicitTier = explicitTier;
            FundingTierMonthlyDollars = fundingTierMonthlyDollars;
        }

        internal GitHubSponsorRecord Value { get; }
        internal int? FundingTierMonthlyDollars { get; }
        internal bool ExplicitTier { get; set; }
        internal bool Excluded { get; set; }
    }
}
