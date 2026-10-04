namespace PowerForge.Generated.Runtime
{
    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Compares an already evaluated collection and candidate using the active host's membership contract.</summary>
        public bool EvaluateMembership(bool ignoreCase, bool negate, object? collection, object? candidate)
        {
            EnsureActive();
            return PowerShellNativeLanguageOperations.EvaluateMembership(_executionContext, ignoreCase, negate, collection, candidate);
        }
    }
}
