namespace PowerForge;

/// <summary>
/// Removes sensitive values from dotnet publish plans before they are displayed or serialized for humans.
/// </summary>
public static class DotNetPublishPlanRedactor
{
    private const string RedactedValue = "<redacted>";

    /// <summary>
    /// Replaces resolved environment variable values in a plan with a redaction marker.
    /// </summary>
    /// <param name="plan">Plan to redact.</param>
    /// <returns>The same plan instance after redaction.</returns>
    public static DotNetPublishPlan RedactInPlace(DotNetPublishPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        Redact(plan.EnvironmentVariables);
        foreach (var step in plan.Steps ?? Array.Empty<DotNetPublishStep>())
            Redact(step.HookEnvironment);

        return plan;
    }

    private static void Redact(System.Collections.IDictionary? variables)
    {
        if (variables is null || variables.Count == 0)
            return;

        foreach (var key in variables.Keys.Cast<object>().ToArray())
        {
            if (variables[key] is not null)
                variables[key] = RedactedValue;
        }
    }
}
