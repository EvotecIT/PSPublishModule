namespace Generic.Semantic.Provider;

public static class NoticeAdapter
{
    public static string Transform(string value) => "provider:" + value;

    public static string[] TransformMany(string value) => new[] { "provider:first:" + value, "provider:second:" + value };

    public static string Fail(string value) => throw new InvalidOperationException("provider-failure:" + value);

    public static string ReturnNull(string value) => null!;

    public static string[] ReturnNullMany(string value) => null!;

    public static string[] ReturnNullItem(string value) => new[] { "provider:" + value, null! };

    public static int ParseInt32(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public static int[] ParseInt32Many(string value)
    {
        var number = ParseInt32(value);
        return new[] { number, number + 1 };
    }

    public static bool ParseBoolean(string value) => bool.Parse(value);

    public static long ParseInt64(string value) => long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public static double ParseDouble(string value) => double.Parse(
        value,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture);

    public static string ReadText(string path) => System.IO.File.ReadAllText(path);

    public static string WaitForCancellation(string value, System.Threading.CancellationToken cancellationToken)
    {
        using (var stream = new System.IO.FileStream(
                   value,
                   System.IO.FileMode.Create,
                   System.IO.FileAccess.ReadWrite,
                   System.IO.FileShare.None))
        {
            stream.WriteByte(1);
            stream.Flush(flushToDisk: true);
            cancellationToken.WaitHandle.WaitOne(System.TimeSpan.FromSeconds(10));
            cancellationToken.ThrowIfCancellationRequested();
            return "not-cancelled:" + value;
        }
    }

    public static string UseFileAndRelease(string path)
    {
        using (var stream = new System.IO.FileStream(
                   path,
                   System.IO.FileMode.OpenOrCreate,
                   System.IO.FileAccess.ReadWrite,
                   System.IO.FileShare.None))
        {
            stream.WriteByte(1);
        }
        return "released:" + path;
    }

    public static string UseFileAndFail(string path)
    {
        using (var stream = new System.IO.FileStream(
                   path,
                   System.IO.FileMode.OpenOrCreate,
                   System.IO.FileAccess.ReadWrite,
                   System.IO.FileShare.None))
        {
            stream.WriteByte(1);
            throw new InvalidOperationException("provider-cleanup-failure:" + path);
        }
    }
}
