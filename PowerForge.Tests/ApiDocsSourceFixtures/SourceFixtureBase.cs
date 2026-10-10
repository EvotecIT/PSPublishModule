namespace PowerForge.Tests.ApiDocsSourceFixtures;

/// <summary>Base type whose public methods are inherited by <see cref="SourceFixtureDerived"/>.</summary>
public class SourceFixtureBase
{
    /// <summary>Inherited member that must not locate the derived type's source.</summary>
    public string Describe() => GetType().Name;
}
