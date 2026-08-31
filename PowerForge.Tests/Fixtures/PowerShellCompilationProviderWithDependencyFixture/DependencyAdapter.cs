namespace Generic.Semantic.Provider;

public static class DependencyAdapter
{
    public static string Transform(string value) => ProviderDependency.Prefix(value);
}
