using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Result returned by Initialize-ModuleRepository for enterprise private-gallery onboarding.
/// </summary>
public sealed class ModuleRepositoryOnboardingResult
{
    /// <summary>Profile name used for onboarding.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Whether the profile was found or created for this onboarding operation.</summary>
    public bool ProfileFound { get; set; }

    /// <summary>Whether the command saved or imported the profile into the current profile store.</summary>
    public bool ProfileWritten { get; set; }

    /// <summary>Whether repository connection was requested.</summary>
    public bool ConnectAttempted { get; set; }

    /// <summary>Whether repository connection was skipped by request or ShouldProcess/WhatIf.</summary>
    public bool ConnectSkipped { get; set; }

    /// <summary>Whether the onboarding operation completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Profile storage path used by PSPublishModule.</summary>
    public string ProfileStorePath { get; set; } = string.Empty;

    /// <summary>Profile storage scope used by PSPublishModule.</summary>
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Imported profile file path when onboarding from a managed profile file.</summary>
    public string? ImportedFromPath { get; set; }

    /// <summary>Saved or imported profile details.</summary>
    public ModuleRepositoryProfileResult? Profile { get; set; }

    /// <summary>Local readiness information for the profile.</summary>
    public ModuleRepositoryProfileReadinessResult? Readiness { get; set; }

    /// <summary>Repository connection result when connection was attempted.</summary>
    public ModuleRepositoryRegistrationResult? Connection { get; set; }

    /// <summary>Recommended wrapper command for installing modules after onboarding.</summary>
    public string RecommendedInstallCommand { get; set; } = string.Empty;

    /// <summary>Recommended wrapper command for updating modules after onboarding.</summary>
    public string RecommendedUpdateCommand { get; set; } = string.Empty;

    /// <summary>Operational messages collected during onboarding.</summary>
    public string[] Messages { get; set; } = System.Array.Empty<string>();
}
