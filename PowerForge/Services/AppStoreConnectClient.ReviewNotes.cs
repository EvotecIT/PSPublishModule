using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Updates only review notes, preserving contacts, demo credentials, and attachments.</summary>
    public async Task<AppStoreConnectReviewDetailsInfo> UpdateReviewNotesAsync(
        string reviewDetailsId, string notes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewDetailsId))
            throw new ArgumentException("Review details id is required.", nameof(reviewDetailsId));
        ValidateReviewNotes(notes);
        var id = reviewDetailsId.Trim();
        var body = new { data = new { type = "appStoreReviewDetails", id, attributes = new { notes } } };
        using var document = await SendJsonAsync(new HttpMethod("PATCH"),
            $"appStoreReviewDetails/{Uri.EscapeDataString(id)}", body, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect returned no review-notes response.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("App Store Connect returned no review-notes data.");
        return ParseReviewDetails(data);
    }

    internal static void ValidateReviewNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || Encoding.UTF8.GetByteCount(notes) > 4000)
            throw new ArgumentException("Review notes must contain text and fit Apple's 4000-byte UTF-8 limit.", nameof(notes));
    }
}
