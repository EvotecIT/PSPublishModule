namespace Generic.Semantic.Provider;

public static class NoticeAdapter
{
    public static string Transform(string value) => "provider:" + value;

    public static string[] TransformMany(string value) => new[] { "provider:first:" + value, "provider:second:" + value };

    public static string Fail(string value) => throw new InvalidOperationException("provider-failure:" + value);

    public static string ReturnNull(string value) => null!;

    public static string[] ReturnNullMany(string value) => null!;

    public static string[] ReturnNullItem(string value) => new[] { "provider:" + value, null! };
}
