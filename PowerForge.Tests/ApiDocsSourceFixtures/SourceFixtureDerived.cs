namespace PowerForge.Tests.ApiDocsSourceFixtures;

/// <summary>Derived type that declares only an auto-property.</summary>
public sealed class SourceFixtureDerived : SourceFixtureBase
{
    /// <summary>Declared member that locates this file.</summary>
    public string? Prompt { get; set; }
}
