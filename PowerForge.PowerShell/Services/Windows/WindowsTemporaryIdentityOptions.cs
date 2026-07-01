namespace PowerForge;

internal sealed class WindowsTemporaryIdentityOptions
{
    internal string UserNamePrefix { get; set; } = "PFTemp";

    internal string ScratchRootPrefix { get; set; } = "pf-temp-user-";

    internal string Description { get; set; } = "Temporary PowerForge user";

    internal string CapabilityName { get; set; } = "temporary Windows identity";
}
