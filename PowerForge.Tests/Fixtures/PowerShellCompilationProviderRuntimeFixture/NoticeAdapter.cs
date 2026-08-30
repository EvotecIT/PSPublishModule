namespace Generic.Semantic.Provider;

public static class NoticeAdapter
{
    public static string Transform(string value) => "provider:" + value;
}
