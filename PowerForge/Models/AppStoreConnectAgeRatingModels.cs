namespace PowerForge;

/// <summary>Publisher answers to Apple's age questionnaire. Null values are unchanged; answers read from Apple submit only edited fields.</summary>
public sealed class AppStoreConnectAgeRatingAnswers
{
    private AppStoreConnectAgeRatingAnswers? _originalAnswers;
    private string? _originalDeclarationId;
    private string? _kidsAgeBand;
    private bool _clearKidsAgeBand;
    private long _kidsAgeBandClearVersion;

    internal AppStoreConnectAgeRatingAnswers? OriginalAnswers => _originalAnswers;
    internal string? OriginalDeclarationId => _originalDeclarationId;
    internal bool KidsAgeBandClearRequested => _clearKidsAgeBand;
    internal long KidsAgeBandClearVersion => _kidsAgeBandClearVersion;

    internal AppStoreConnectAgeRatingAnswers CreateSubmissionSnapshot()
        => (AppStoreConnectAgeRatingAnswers)MemberwiseClone();

    internal void CaptureOriginalAnswers(string declarationId)
    {
        var original = (AppStoreConnectAgeRatingAnswers)MemberwiseClone();
        original._originalAnswers = null;
        _originalAnswers = original;
        _originalDeclarationId = declarationId;
    }

    internal void AcceptSubmission(AppStoreConnectAgeRatingAnswers submitted, string declarationId)
    {
        submitted.CaptureOriginalAnswers(declarationId);
        _originalAnswers = submitted._originalAnswers;
        _originalDeclarationId = declarationId;
    }

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

    /// <summary>Optional Made for Kids age band: FIVE_AND_UNDER, SIX_TO_EIGHT, or NINE_TO_ELEVEN. Null leaves the band unchanged; use <see cref="ClearKidsAgeBand"/> to remove it.</summary>
    public string? KidsAgeBand
    {
        get => _kidsAgeBand;
        set
        {
            _kidsAgeBand = value;
            _clearKidsAgeBand = false;
        }
    }

    /// <summary>Requests an explicit null Made for Kids age band. Successful submission suppresses the same clear on that declaration; a different declaration or another call requests a new clear. Apple permits changing this selection only before App Review approval.</summary>
    public void ClearKidsAgeBand()
    {
        _kidsAgeBand = null;
        _clearKidsAgeBand = true;
        _kidsAgeBandClearVersion++;
    }

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
