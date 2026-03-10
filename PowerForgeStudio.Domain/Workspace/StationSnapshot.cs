namespace PowerForgeStudio.Domain.Workspace;

public sealed record StationSnapshot<TItem>(
    string Headline,
    string Details,
    IReadOnlyList<TItem> Items);
