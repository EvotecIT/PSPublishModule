namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>
    /// MSBuild property set to <c>true</c> on every restore, build, pack and property evaluation of a repository
    /// release. A project can keep packaging-only settings, such as the runtime identifiers of a RID-specific .NET
    /// tool, out of ordinary restores and builds by conditioning them on it.
    /// </summary>
    public const string PackagingPropertyName = "PowerForgePackaging";

    private const string PackagingPropertyArgument = "-p:" + PackagingPropertyName + "=true";
}
