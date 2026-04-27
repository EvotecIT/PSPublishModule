namespace PowerForge;

/// <summary>
/// Strategy used to pack selected .NET repository projects.
/// </summary>
public enum DotNetRepositoryPackStrategy
{
    /// <summary>Run <c>dotnet pack</c> once per selected project.</summary>
    PerProject,

    /// <summary>Generate a temporary MSBuild traversal project and pack selected projects in one invocation.</summary>
    MSBuild
}
