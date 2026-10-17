using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Reads the age declaration for one App Information resource, preserving unanswered values.</summary>
    public Task<AppStoreConnectAgeRatingDeclarationInfo?> GetAgeRatingDeclarationAsync(
        string appInfoId, CancellationToken cancellationToken = default)
    {
        RequireValue(appInfoId, nameof(appInfoId), "App Information id");
        return GetSingleAsync($"appInfos/{Uri.EscapeDataString(appInfoId.Trim())}/ageRatingDeclaration", ParseAgeRatingDeclaration, cancellationToken);
    }

    /// <summary>Updates supplied human-reviewed answers, or only edited fields of a read snapshot. This never chooses an age override.</summary>
    public async Task<AppStoreConnectAgeRatingDeclarationInfo> UpdateAgeRatingDeclarationAsync(
        string declarationId, AppStoreConnectAgeRatingAnswers answers, CancellationToken cancellationToken = default)
    {
        RequireValue(declarationId, nameof(declarationId), "Age declaration id");
        if (answers is null) throw new ArgumentNullException(nameof(answers));
        var id = declarationId.Trim();
        var submitted = answers.CreateSubmissionSnapshot();
        var attributes = BuildAgeRatingAttributes(submitted, id);
        if (attributes.Count == 0) throw new ArgumentException("Supply at least one explicit age questionnaire answer.", nameof(answers));
        using var document = await SendJsonAsync(new HttpMethod("PATCH"), $"ageRatingDeclarations/{Uri.EscapeDataString(id)}",
            new { data = new { type = "ageRatingDeclarations", id, attributes } }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Apple returned no age declaration response.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Apple returned no age declaration data.");
        var declaration = ParseAgeRatingDeclaration(data);
        answers.AcceptSubmission(submitted, id);
        return declaration;
    }

    private static AppStoreConnectAgeRatingDeclarationInfo ParseAgeRatingDeclaration(JsonElement data)
    {
        var attributes = GetAttributes(data);
        var declaration = new AppStoreConnectAgeRatingDeclarationInfo
        {
            Id = GetString(data, "id") ?? string.Empty,
            Answers = new AppStoreConnectAgeRatingAnswers
            {
                Advertising = GetBool(attributes, "advertising"),
                AlcoholTobaccoOrDrugUseOrReferences = GetString(attributes, "alcoholTobaccoOrDrugUseOrReferences"),
                Contests = GetString(attributes, "contests"),
                DeveloperAgeRatingInfoUrl = GetString(attributes, "developerAgeRatingInfoUrl"),
                Gambling = GetBool(attributes, "gambling"),
                GamblingSimulated = GetString(attributes, "gamblingSimulated"),
                GunsOrOtherWeapons = GetString(attributes, "gunsOrOtherWeapons"),
                HealthOrWellnessTopics = GetBool(attributes, "healthOrWellnessTopics"),
                KidsAgeBand = GetString(attributes, "kidsAgeBand"),
                LootBox = GetBool(attributes, "lootBox"),
                MedicalOrTreatmentInformation = GetString(attributes, "medicalOrTreatmentInformation"),
                MessagingAndChat = GetBool(attributes, "messagingAndChat"),
                ParentalControls = GetBool(attributes, "parentalControls"),
                ProfanityOrCrudeHumor = GetString(attributes, "profanityOrCrudeHumor"),
                AgeAssurance = GetBool(attributes, "ageAssurance"),
                SexualContentGraphicAndNudity = GetString(attributes, "sexualContentGraphicAndNudity"),
                SexualContentOrNudity = GetString(attributes, "sexualContentOrNudity"),
                SocialMedia = GetBool(attributes, "socialMedia"),
                SocialMediaAgeRestricted = GetBool(attributes, "socialMediaAgeRestricted"),
                HorrorOrFearThemes = GetString(attributes, "horrorOrFearThemes"),
                MatureOrSuggestiveThemes = GetString(attributes, "matureOrSuggestiveThemes"),
                UnrestrictedWebAccess = GetBool(attributes, "unrestrictedWebAccess"),
                UserGeneratedContent = GetBool(attributes, "userGeneratedContent"),
                ViolenceCartoonOrFantasy = GetString(attributes, "violenceCartoonOrFantasy"),
                ViolenceRealisticProlongedGraphicOrSadistic = GetString(attributes, "violenceRealisticProlongedGraphicOrSadistic"),
                ViolenceRealistic = GetString(attributes, "violenceRealistic"),
            }
        };
        declaration.Answers.CaptureOriginalAnswers(declaration.Id);
        return declaration;
    }

    private static Dictionary<string, object?> BuildAgeRatingAttributes(AppStoreConnectAgeRatingAnswers answers, string declarationId)
    {
        var attributes = ReadAgeRatingAttributes(answers);
        var originalAnswers = string.Equals(answers.OriginalDeclarationId, declarationId, StringComparison.Ordinal)
            ? answers.OriginalAnswers : null;
        if (originalAnswers is not null)
        {
            var original = ReadAgeRatingAttributes(originalAnswers);
            foreach (var answer in attributes.ToArray())
                if (original.TryGetValue(answer.Key, out var previous) && Equals(previous, answer.Value))
                    attributes.Remove(answer.Key);
        }
        if (answers.KidsAgeBandClearRequested &&
            (originalAnswers?.KidsAgeBandClearRequested != true ||
             originalAnswers.KidsAgeBandClearVersion != answers.KidsAgeBandClearVersion))
            attributes["kidsAgeBand"] = null;
        if (answers.DeveloperAgeRatingInfoUrlClearRequested &&
            (originalAnswers?.DeveloperAgeRatingInfoUrlClearRequested != true ||
             originalAnswers.DeveloperAgeRatingInfoUrlClearVersion != answers.DeveloperAgeRatingInfoUrlClearVersion))
            attributes["developerAgeRatingInfoUrl"] = null;
        foreach (var answer in attributes)
        {
            if (answer.Value is not string value) continue;
            if (answer.Key == "developerAgeRatingInfoUrl")
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString())
                    throw new ArgumentException("Developer age-rating information URL must be a well-formed absolute URI.", nameof(answers));
                continue;
            }
            var valid = answer.Key == "kidsAgeBand"
                ? value is "FIVE_AND_UNDER" or "SIX_TO_EIGHT" or "NINE_TO_ELEVEN"
                : value is "NONE" or "INFREQUENT_OR_MILD" or "FREQUENT_OR_INTENSE" or "INFREQUENT" or "FREQUENT";
            if (!valid)
                throw new ArgumentException($"Unsupported age answer '{value}' for '{answer.Key}'.", nameof(answers));
        }
        return attributes;
    }

    private static Dictionary<string, object?> ReadAgeRatingAttributes(AppStoreConnectAgeRatingAnswers answers)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddIfNotNull(attributes, "advertising", answers.Advertising);
        AddIfNotNull(attributes, "alcoholTobaccoOrDrugUseOrReferences", answers.AlcoholTobaccoOrDrugUseOrReferences);
        AddIfNotNull(attributes, "contests", answers.Contests);
        AddIfNotNull(attributes, "developerAgeRatingInfoUrl", answers.DeveloperAgeRatingInfoUrl);
        AddIfNotNull(attributes, "gambling", answers.Gambling);
        AddIfNotNull(attributes, "gamblingSimulated", answers.GamblingSimulated);
        AddIfNotNull(attributes, "gunsOrOtherWeapons", answers.GunsOrOtherWeapons);
        AddIfNotNull(attributes, "healthOrWellnessTopics", answers.HealthOrWellnessTopics);
        AddIfNotNull(attributes, "kidsAgeBand", answers.KidsAgeBand);
        AddIfNotNull(attributes, "lootBox", answers.LootBox);
        AddIfNotNull(attributes, "medicalOrTreatmentInformation", answers.MedicalOrTreatmentInformation);
        AddIfNotNull(attributes, "messagingAndChat", answers.MessagingAndChat);
        AddIfNotNull(attributes, "parentalControls", answers.ParentalControls);
        AddIfNotNull(attributes, "profanityOrCrudeHumor", answers.ProfanityOrCrudeHumor);
        AddIfNotNull(attributes, "ageAssurance", answers.AgeAssurance);
        AddIfNotNull(attributes, "sexualContentGraphicAndNudity", answers.SexualContentGraphicAndNudity);
        AddIfNotNull(attributes, "sexualContentOrNudity", answers.SexualContentOrNudity);
        AddIfNotNull(attributes, "socialMedia", answers.SocialMedia);
        AddIfNotNull(attributes, "socialMediaAgeRestricted", answers.SocialMediaAgeRestricted);
        AddIfNotNull(attributes, "horrorOrFearThemes", answers.HorrorOrFearThemes);
        AddIfNotNull(attributes, "matureOrSuggestiveThemes", answers.MatureOrSuggestiveThemes);
        AddIfNotNull(attributes, "unrestrictedWebAccess", answers.UnrestrictedWebAccess);
        AddIfNotNull(attributes, "userGeneratedContent", answers.UserGeneratedContent);
        AddIfNotNull(attributes, "violenceCartoonOrFantasy", answers.ViolenceCartoonOrFantasy);
        AddIfNotNull(attributes, "violenceRealisticProlongedGraphicOrSadistic", answers.ViolenceRealisticProlongedGraphicOrSadistic);
        AddIfNotNull(attributes, "violenceRealistic", answers.ViolenceRealistic);
        return attributes;
    }

    private static void AddIfNotNull(Dictionary<string, object?> attributes, string name, bool? value)
    {
        if (value.HasValue) attributes.Add(name, value.Value);
    }
}
