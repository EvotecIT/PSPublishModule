namespace PowerForgeStudio.Domain.Hub;

public sealed record DiffLine(string Text, DiffLineKind Kind)
{
    public static DiffLine Parse(string line)
    {
        if (line.StartsWith('+') && !line.StartsWith("+++"))
        {
            return new DiffLine(line, DiffLineKind.Added);
        }

        if (line.StartsWith('-') && !line.StartsWith("---"))
        {
            return new DiffLine(line, DiffLineKind.Removed);
        }

        if (line.StartsWith("@@"))
        {
            return new DiffLine(line, DiffLineKind.Hunk);
        }

        return new DiffLine(line, DiffLineKind.Context);
    }
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Hunk
}
