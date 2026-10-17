namespace PowerForge;

/// <summary>Explicit publisher answers to Apple's age questionnaire. Null values are left unchanged; no answer is inferred.</summary>
public sealed class AppStoreConnectAgeRatingAnswers
{
    /// <summary>Publisher answer for Apple's <c>advertising</c> question.</summary>
    public bool? Advertising { get; set; }

    /// <summary>Publisher answer for Apple's <c>alcoholTobaccoOrDrugUseOrReferences</c> question.</summary>
    public string? AlcoholTobaccoOrDrugUseOrReferences { get; set; }

    /// <summary>Publisher answer for Apple's <c>contests</c> question.</summary>
    public string? Contests { get; set; }

    /// <summary>Publisher answer for Apple's <c>gambling</c> question.</summary>
    public bool? Gambling { get; set; }

    /// <summary>Publisher answer for Apple's <c>gamblingSimulated</c> question.</summary>
    public string? GamblingSimulated { get; set; }

    /// <summary>Publisher answer for Apple's <c>gunsOrOtherWeapons</c> question.</summary>
    public string? GunsOrOtherWeapons { get; set; }

    /// <summary>Publisher answer for Apple's <c>healthOrWellnessTopics</c> question.</summary>
    public bool? HealthOrWellnessTopics { get; set; }

    /// <summary>Publisher answer for Apple's <c>lootBox</c> question.</summary>
    public bool? LootBox { get; set; }

    /// <summary>Publisher answer for Apple's <c>medicalOrTreatmentInformation</c> question.</summary>
    public string? MedicalOrTreatmentInformation { get; set; }

    /// <summary>Publisher answer for Apple's <c>messagingAndChat</c> question.</summary>
    public bool? MessagingAndChat { get; set; }

    /// <summary>Publisher answer for Apple's <c>parentalControls</c> question.</summary>
    public bool? ParentalControls { get; set; }

    /// <summary>Publisher answer for Apple's <c>profanityOrCrudeHumor</c> question.</summary>
    public string? ProfanityOrCrudeHumor { get; set; }

    /// <summary>Publisher answer for Apple's <c>ageAssurance</c> question.</summary>
    public bool? AgeAssurance { get; set; }

    /// <summary>Publisher answer for Apple's <c>sexualContentGraphicAndNudity</c> question.</summary>
    public string? SexualContentGraphicAndNudity { get; set; }

    /// <summary>Publisher answer for Apple's <c>sexualContentOrNudity</c> question.</summary>
    public string? SexualContentOrNudity { get; set; }

    /// <summary>Publisher answer for Apple's <c>socialMedia</c> question.</summary>
    public bool? SocialMedia { get; set; }

    /// <summary>Publisher answer for Apple's <c>socialMediaAgeRestricted</c> question.</summary>
    public bool? SocialMediaAgeRestricted { get; set; }

    /// <summary>Publisher answer for Apple's <c>horrorOrFearThemes</c> question.</summary>
    public string? HorrorOrFearThemes { get; set; }

    /// <summary>Publisher answer for Apple's <c>matureOrSuggestiveThemes</c> question.</summary>
    public string? MatureOrSuggestiveThemes { get; set; }

    /// <summary>Publisher answer for Apple's <c>unrestrictedWebAccess</c> question.</summary>
    public bool? UnrestrictedWebAccess { get; set; }

    /// <summary>Publisher answer for Apple's <c>userGeneratedContent</c> question.</summary>
    public bool? UserGeneratedContent { get; set; }

    /// <summary>Publisher answer for Apple's <c>violenceCartoonOrFantasy</c> question.</summary>
    public string? ViolenceCartoonOrFantasy { get; set; }

    /// <summary>Publisher answer for Apple's <c>violenceRealisticProlongedGraphicOrSadistic</c> question.</summary>
    public string? ViolenceRealisticProlongedGraphicOrSadistic { get; set; }

    /// <summary>Publisher answer for Apple's <c>violenceRealistic</c> question.</summary>
    public string? ViolenceRealistic { get; set; }

}

/// <summary>An existing Apple age declaration and its explicit questionnaire answers.</summary>
public sealed class AppStoreConnectAgeRatingDeclarationInfo
{
    /// <summary>Opaque declaration identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Current publisher answers, including unanswered null values.</summary>
    public AppStoreConnectAgeRatingAnswers Answers { get; set; } = new();
}
