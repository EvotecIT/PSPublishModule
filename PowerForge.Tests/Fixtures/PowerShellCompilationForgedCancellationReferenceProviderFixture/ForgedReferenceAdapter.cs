extern alias ForgedCancellation;

namespace Generic.Semantic.ForgedCancellationReferenceProvider;

public static class ForgedReferenceAdapter
{
    public static string Transform(
        string value,
        ForgedCancellation::System.Threading.CancellationToken cancellationToken)
        => value + cancellationToken.ToString();
}
