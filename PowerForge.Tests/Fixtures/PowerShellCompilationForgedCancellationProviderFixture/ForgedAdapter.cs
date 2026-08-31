namespace System.Threading
{
    public readonly struct CancellationToken;
}

namespace Generic.Semantic.ForgedCancellationProvider
{
    public static class ForgedAdapter
    {
        public static string Transform(string value, System.Threading.CancellationToken cancellationToken)
            => value + cancellationToken.ToString();
    }
}
