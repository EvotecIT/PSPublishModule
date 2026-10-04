using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class CatalogUpdateService
{
    /// <summary>Creates a durable intent that CI must archive before the execution step.</summary>
    public CatalogUpdateReceipt Reserve(string output, string profilePath, string releaseConfigPath, string channel, string reservationKey, string? storeConfigPath = null)
    {
        if (channel is not ("winget" or "store" or "all") || !Regex.IsMatch(reservationKey, "^[a-fA-F0-9]{32}$"))
            throw new ArgumentException("Reserve requires a channel and a fresh 32-character hexadecimal reservation key.");
        using var fileLock = new FileStream(Path.Combine(output, "catalog-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var receipt = Read(output, profilePath, releaseConfigPath);
        var channels = channel == "all" ? new[] { receipt.Winget, receipt.Store } : new[] { channel == "winget" ? receipt.Winget : receipt.Store };
        foreach (var selected in channels.Where(value => value.State != "Submitted")) RequirePrepared(selected, "Catalog");
        if (channels.Contains(receipt.Store) && receipt.Store.State != "Submitted")
        {
            if (string.IsNullOrWhiteSpace(storeConfigPath)) throw new InvalidOperationException("Store reservation requires its configuration.");
            receipt.Store.ConfigurationSha256 = Hash(storeConfigPath!);
        }
        foreach (var selected in channels.Where(value => value.State != "Submitted"))
        { selected.State = "Reserved"; selected.ReservationKey = reservationKey; }
        Save(output, receipt);
        return receipt;
    }

    /// <summary>Records an operator-verified remote result after an uncertain attempt; it never retries the mutation.</summary>
    public CatalogUpdateReceipt Reconcile(string output, string profilePath, string releaseConfigPath,
        string channel, string reference, bool confirmed, string? storeConfigPath = null)
    {
        if (!confirmed) throw new InvalidOperationException("Reconciliation requires --confirm-reconciled after checking the exact package version and installer URLs in the remote service.");
        using var fileLock = new FileStream(Path.Combine(output, "catalog-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var receipt = Read(output, profilePath, releaseConfigPath);
        var selected = channel switch { "winget" => receipt.Winget, "store" => receipt.Store, _ => throw new ArgumentException("Reconcile one channel: winget or store.") };
        if (selected.State is not ("Reserved" or "Attempting")) throw new InvalidOperationException("Only a reserved or uncertain submission attempt can be reconciled.");
        if (reference == "none")
        {
            selected.State = "Prepared"; selected.ReservationKey = null;
            selected.Reference = null; selected.RemoteStatus = "OperatorConfirmedNoSubmission";
            Save(output, receipt); return receipt;
        }
        if (channel == "winget" ? !Regex.IsMatch(reference, @"^https://github\.com/microsoft/winget-pkgs/pull/[0-9]+$", RegexOptions.CultureInvariant) :
            !Regex.IsMatch(reference, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid catalog submission reference.");
        if (channel == "store")
        {
            if (string.IsNullOrWhiteSpace(storeConfigPath)) throw new InvalidOperationException("Store reconciliation requires the configuration used for the submission.");
            selected.ConfigurationSha256 = Hash(storeConfigPath!);
        }
        selected.Reference = reference;
        selected.State = "Submitted";
        selected.ReservationKey = null;
        selected.RemoteStatus = "OperatorReconciled";
        Save(output, receipt);
        return receipt;
    }

    private static async Task<string> ReadWingetStatusAsync(string reference, CancellationToken cancellationToken)
    {
        var match = Regex.Match(reference, @"^https://github\.com/microsoft/winget-pkgs/pull/([0-9]+)$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidOperationException("Invalid WinGet PR reference in catalog receipt.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/microsoft/winget-pkgs/pulls/" + match.Groups[1].Value);
        request.Headers.UserAgent.ParseAdd("PowerForge-Catalog/1.0");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return payload.RootElement.GetProperty("merged_at").ValueKind != JsonValueKind.Null ? "Merged" : payload.RootElement.GetProperty("state").GetString() ?? "Unknown";
    }
}
