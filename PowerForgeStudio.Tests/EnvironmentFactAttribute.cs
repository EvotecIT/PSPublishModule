namespace PowerForgeStudio.Tests;

internal sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(string variableName)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(variableName),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {variableName}=true to run this product smoke.";
        }
    }
}
