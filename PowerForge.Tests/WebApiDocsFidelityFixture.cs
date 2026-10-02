namespace PowerForge.Tests.DocsFidelity;

// Emulate the virtual document paths emitted by Roslyn's deterministic PathMap.
#line 1 "/_/Fixtures/DocsFidelityFixture.cs"
public sealed class Fixture
{
    public Fixture(string? name = null) { Field = name; }
    public string? Create(string? text = null) => text;
    public Fixture? Open(Fixture? options = null) => options;
    public string?[,]? Matrix { get; set; }
    public Dictionary<string, List<string?>?>? Values { get; set; }
    public string? Field;
    public event Action<string?>? Changed { add { } remove { } }
#line 1 "/_/Fixtures/obj/Release/Generated.g.cs"
    public string Generated() => "generated";
}
#line default

#line 1 "/_/Fixtures/DocsFidelityFixture.cs"
public static class FixtureExtensions
{
    public static string? Echo(this Fixture fixture, string? text) => text;
}
#line default
