namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ISet<string> CompilerResourceOperandAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EmbedResources",
            "LinkResources",
            "Resources",
            "SourceModules"
        };

    private static bool TryNormalizeControlledCompilerTaskFileOperand(
        string taskName,
        string attributeName,
        string operand,
        out string path)
    {
        path = operand;
        if (!IsCompilerTask(taskName))
            return true;

        if (CompilerResourceOperandAttributes.Contains(attributeName))
        {
            int separator = path.IndexOf(',');
            if (separator >= 0)
                path = path.Substring(0, separator).Trim().Trim('\'', '"');
        }

        if (attributeName.Equals("References", StringComparison.OrdinalIgnoreCase))
        {
            if (path.IndexOf('=') >= 0)
                return false;
        }

        return path.Length > 0 &&
               path.IndexOf(',') < 0 &&
               path.IndexOf('=') < 0;
    }

    private static bool IsCompilerTask(string taskName)
        => taskName.Equals("AL", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Csc", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Fsc", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Vbc", StringComparison.OrdinalIgnoreCase);
}
